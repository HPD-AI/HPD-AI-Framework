// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using HPD.Agent.Audio.Interruption;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Preemptive;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Turn;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;
using HPD.Agent.Audio.Eot;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using HPD.Events;
using HPD.Events.Struct;

namespace HPD.Agent.Audio;

/// <summary>
/// Middleware that orchestrates STT → LLM → TTS pipeline.
/// Uses IAgentMiddleware hooks for streaming interception.
/// </summary>
public partial class AudioPipelineMiddleware : IAgentMiddleware
{
    //
    // CONFIGURATION (uses AudioConfig for all settings)
    //

    /// <summary>
    /// Middleware-level default configuration.
    /// Per-request overrides via AudioConfig are merged with these defaults.
    /// </summary>
    private AudioConfig _config = new();

    /// <summary>
    /// Creates a middleware instance with default audio configuration.
    /// </summary>
    public AudioPipelineMiddleware()
    {
        ProviderRegistry = new AgentBuilder().ProviderRegistry;
    }

    /// <summary>
    /// Creates a middleware instance with the supplied audio configuration as middleware defaults.
    /// </summary>
    public AudioPipelineMiddleware(AudioConfig config)
    {
        ProviderRegistry = new AgentBuilder().ProviderRegistry;
        Configure(config);
    }

    /// <summary>
    /// Replaces middleware-level default audio configuration.
    /// </summary>
    public void Configure(AudioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config.Clone();
    }

    //
    // PROCESSING MODE (HOW audio is processed)
    //

    /// <summary>How audio is processed internally. Default: Pipeline.</summary>
    public AudioProcessingMode ProcessingMode
    {
        get => _config.ProcessingMode;
        set => _config.ProcessingMode = value;
    }

    //
    // I/O MODE (WHAT goes in/out)
    //

    /// <summary>What input/output modalities to use. Default: AudioToAudioAndText.</summary>
    public AudioIOMode IOMode
    {
        get => _config.IOMode;
        set => _config.IOMode = value;
    }

    // Derived helpers for checking I/O capabilities
    /// <summary>Whether audio input is expected.</summary>
    public bool HasAudioInput => HasAudioInputMode(IOMode);

    /// <summary>Whether audio output is enabled.</summary>
    public bool HasAudioOutput => HasAudioOutputMode(IOMode);

    /// <summary>Whether text output is enabled (in addition to or instead of audio).</summary>
    public bool HasTextOutput => HasTextOutputMode(IOMode);

    //
    // PROVIDERS (injected)
    //

    /// <summary>HPD speech recognizer.</summary>
    public ISpeechRecognizer? SpeechRecognizer { get; set; }

    /// <summary>TTS client.</summary>
    public ITextToSpeechClient? TextToSpeechClient { get; set; }

    /// <summary>Voice activity detector (optional, for fast interruption).</summary>
    public IVoiceActivityDetector? Vad { get; set; }

    /// <summary>End-of-turn detector.</summary>
    public IEotDetector? EotDetector { get; set; }

    /// <summary>Unified provider registry used to resolve configured audio client families.</summary>
    public IProviderRegistry? ProviderRegistry { get; set; }

    //
    // QUICK ANSWER (delegates to AudioPipelineConfig)
    //

    /// <summary>Enable TTS on first complete sentence. Default: true.</summary>
    public bool EnableQuickAnswer
    {
        get => _config.EnableQuickAnswer ?? true;
        set => _config.EnableQuickAnswer = value;
    }

    //
    // SPEED ADAPTATION (delegates to AudioPipelineConfig)
    //

    /// <summary>Enable adaptive endpointing based on user speaking speed. Default: true.</summary>
    public bool EnableSpeedAdaptation
    {
        get => _config.EnableSpeedAdaptation ?? true;
        set => _config.EnableSpeedAdaptation = value;
    }

    private float _currentWpm = 150f; // Internal state
    private TurnMetrics? _turnMetrics; // Metrics for current turn
    private readonly object _runtimeInputLock = new();
    private readonly Dictionary<AudioInputKey, AudioInputBuffer> _runtimeInputBuffers = new();

    // FALSE INTERRUPTION RECOVERY STATE
    private PausedSynthesisState? _pausedSynthesis;
    private readonly object _pauseLock = new();
    private readonly object _interruptionLock = new();
    private readonly Dictionary<string, ISpeechOutputSession> _activeOutputSessions = new();
    private InterruptionController? _interruptionController;

    // FILLER AUDIO STATE
    private List<CachedFillerAudio>? _cachedFillers;
    private CancellationTokenSource? _fillerCts;
    private Task? _fillerTask;

    /// <summary>Current estimated user words-per-minute.</summary>
    public float CurrentWpm => _currentWpm;

    //
    // BACKCHANNEL DETECTION (delegates to AudioPipelineConfig)
    //

    /// <summary>How to handle short utterances during bot speech. Default: IgnoreShortUtterances.</summary>
    public BackchannelStrategy BackchannelStrategy
    {
        get => _config.BackchannelStrategy ?? BackchannelStrategy.IgnoreShortUtterances;
        set => _config.BackchannelStrategy = value;
    }

    /// <summary>Minimum words required to trigger interruption. Default: 2.</summary>
    public int MinWordsForInterruption
    {
        get => _config.MinWordsForInterruption ?? 2;
        set => _config.MinWordsForInterruption = value;
    }

    //
    // FILLER AUDIO (delegates to AudioPipelineConfig)
    //

    /// <summary>Enable filler audio during LLM thinking. Default: false.</summary>
    public bool EnableFillerAudio
    {
        get => _config.EnableFillerAudio ?? false;
        set => _config.EnableFillerAudio = value;
    }

    /// <summary>Silence duration before playing filler. Default: 1.5s.</summary>
    public float FillerSilenceThreshold
    {
        get => _config.FillerSilenceThreshold ?? 1.5f;
        set => _config.FillerSilenceThreshold = value;
    }

    /// <summary>Filler phrases to synthesize. Default: ["Um...", "Let me see..."].</summary>
    public string[] FillerPhrases
    {
        get => _config.FillerPhrases ?? ["Um...", "Let me see...", "One moment..."];
        set => _config.FillerPhrases = value;
    }

    //
    // TEXT FILTERING (delegates to AudioPipelineConfig)
    //

    /// <summary>Enable filtering of markdown/code from TTS input. Default: true.</summary>
    public bool EnableTextFiltering
    {
        get => _config.EnableTextFiltering ?? true;
        set => _config.EnableTextFiltering = value;
    }

    /// <summary>Remove code blocks (```...```) from TTS. Default: true.</summary>
    public bool FilterCodeBlocks
    {
        get => _config.FilterCodeBlocks ?? true;
        set => _config.FilterCodeBlocks = value;
    }

    /// <summary>Remove markdown tables from TTS. Default: true.</summary>
    public bool FilterTables
    {
        get => _config.FilterTables ?? true;
        set => _config.FilterTables = value;
    }

    /// <summary>Remove URLs from TTS (speaks domain only). Default: true.</summary>
    public bool FilterUrls
    {
        get => _config.FilterUrls ?? true;
        set => _config.FilterUrls = value;
    }

    /// <summary>Remove markdown formatting (**bold**, *italic*, etc). Default: true.</summary>
    public bool FilterMarkdownFormatting
    {
        get => _config.FilterMarkdownFormatting ?? true;
        set => _config.FilterMarkdownFormatting = value;
    }

    /// <summary>Remove emoji characters from TTS. Default: true. ( )</summary>
    public bool FilterEmoji
    {
        get => _config.FilterEmoji ?? true;
        set => _config.FilterEmoji = value;
    }

    //
    // FALSE INTERRUPTION RECOVERY (delegates to AudioPipelineConfig)
    //

    /// <summary>Enable false interruption recovery. Default: true. ( )</summary>
    public bool EnableFalseInterruptionRecovery
    {
        get => _config.EnableFalseInterruptionRecovery ?? true;
        set => _config.EnableFalseInterruptionRecovery = value;
    }

    /// <summary>Time to wait for transcript after interruption before resuming. Default: 2.0s. ( )</summary>
    public float FalseInterruptionTimeout
    {
        get => _config.FalseInterruptionTimeout ?? 2.0f;
        set => _config.FalseInterruptionTimeout = value;
    }

    /// <summary>Resume paused speech if no transcript received. Default: true. ( )</summary>
    public bool ResumeFalseInterruption
    {
        get => _config.ResumeFalseInterruption ?? true;
        set => _config.ResumeFalseInterruption = value;
    }

    //
    // PREEMPTIVE GENERATION (delegates to AudioPipelineConfig)
    //

    /// <summary>Start LLM inference before EOT is confirmed. Reduces latency but uses more compute. Default: false.</summary>
    public bool EnablePreemptiveGeneration
    {
        get => _config.EnablePreemptiveGeneration ?? false;
        set => _config.EnablePreemptiveGeneration = value;
    }

    /// <summary>Minimum EOT probability to trigger preemptive generation. Default: 0.7.</summary>
    public float PreemptiveGenerationThreshold
    {
        get => _config.PreemptiveGenerationThreshold ?? 0.7f;
        set => _config.PreemptiveGenerationThreshold = value;
    }

    //
    // TTS DEFAULTS (delegates to AudioConfig.Tts)
    //

    /// <summary>Default TTS voice.</summary>
    public string? DefaultVoice
    {
        get => _config.Tts?.Voice;
        set
        {
            _config.Tts ??= new Tts.TtsConfig();
            _config.Tts.Voice = value;
        }
    }

    /// <summary>Default TTS model.</summary>
    public string? DefaultModel
    {
        get => _config.Tts?.ModelId;
        set
        {
            _config.Tts ??= new Tts.TtsConfig();
            _config.Tts.ModelId = value;
        }
    }

    /// <summary>Default TTS output format.</summary>
    public string? DefaultOutputFormat
    {
        get => _config.Tts?.OutputFormat;
        set
        {
            _config.Tts ??= new Tts.TtsConfig();
            _config.Tts.OutputFormat = value;
        }
    }

    /// <summary>Default TTS sample rate.</summary>
    public int? DefaultSampleRate
    {
        get => _config.Tts?.SampleRate;
        set
        {
            _config.Tts ??= new Tts.TtsConfig();
            _config.Tts.SampleRate = value;
        }
    }

    //
    // CONFIGURATION HELPERS
    //

    /// <summary>
    /// Gets the effective configuration by merging per-request overrides with middleware defaults.
    /// </summary>
    private AudioConfig GetEffectiveConfig(object? audioOptions) =>
        audioOptions switch
        {
            AudioRunConfig runOptions => GetEffectiveConfig(runOptions),
            AudioConfig config => _config.MergeWith(config),
            _ => _config.Clone()
        };

    private AudioConfig GetEffectiveConfig(AudioRunConfig runOptions)
    {
        runOptions.Validate();

        var effective = _config.Clone();
        if (runOptions.ProcessingMode.HasValue)
            effective.ProcessingMode = runOptions.ProcessingMode.Value;

        if (runOptions.IOMode.HasValue)
            effective.IOMode = runOptions.IOMode.Value;

        effective.Language = runOptions.Language ?? effective.Language;
        effective.Disabled = runOptions.Disabled ?? effective.Disabled;

        var runTts = AudioConfig.CloneTts(runOptions.Tts);
        if (runOptions.Voice != null || runOptions.TtsModel != null || runOptions.TtsSpeed != null)
        {
            runTts ??= new TtsConfig();
            runTts.Voice ??= runOptions.Voice;
            runTts.ModelId ??= runOptions.TtsModel;
            if (runOptions.TtsSpeed.HasValue)
                runTts.Speed ??= runOptions.TtsSpeed.Value;
        }

        effective.Tts = MergeTts(effective.Tts, runTts);
        effective.Stt = MergeStt(effective.Stt, runOptions.Stt);
        effective.Vad = runOptions.Vad ?? effective.Vad;

        return effective;
    }

    private static TtsConfig? MergeTts(TtsConfig? defaults, TtsConfig? overrides)
    {
        if (overrides == null)
            return defaults;

        defaults = AudioConfig.CloneTts(defaults) ?? new TtsConfig();
        defaults.Voice = overrides.Voice ?? defaults.Voice;
        defaults.Speed = overrides.Speed ?? defaults.Speed;
        defaults.Pitch = overrides.Pitch ?? defaults.Pitch;
        defaults.Volume = overrides.Volume ?? defaults.Volume;
        defaults.OutputFormat = overrides.OutputFormat ?? defaults.OutputFormat;
        defaults.SampleRate = overrides.SampleRate ?? defaults.SampleRate;
        defaults.ModelId = overrides.ModelId ?? defaults.ModelId;
        defaults.Language = overrides.Language ?? defaults.Language;
        defaults.Provider = string.IsNullOrWhiteSpace(overrides.Provider) ? defaults.Provider : overrides.Provider;
        defaults.ProviderOptionsJson = overrides.ProviderOptionsJson ?? defaults.ProviderOptionsJson;
        defaults.OverrideClient = overrides.OverrideClient ?? defaults.OverrideClient;
        defaults.AdditionalProperties = MergeAdditionalProperties(defaults.AdditionalProperties, overrides.AdditionalProperties);
        return defaults;
    }

    private static SttConfig? MergeStt(SttConfig? defaults, SttConfig? overrides)
    {
        if (overrides == null)
            return defaults;

        defaults = AudioConfig.CloneStt(defaults) ?? new SttConfig();
        defaults.Language = overrides.Language ?? defaults.Language;
        defaults.SpeechSampleRate = overrides.SpeechSampleRate ?? defaults.SpeechSampleRate;
        defaults.TextLanguage = overrides.TextLanguage ?? defaults.TextLanguage;
        defaults.ModelId = overrides.ModelId ?? defaults.ModelId;
        defaults.Temperature = overrides.Temperature ?? defaults.Temperature;
        defaults.ResponseFormat = overrides.ResponseFormat ?? defaults.ResponseFormat;
        defaults.Provider = string.IsNullOrWhiteSpace(overrides.Provider) ? defaults.Provider : overrides.Provider;
        defaults.ProviderOptionsJson = overrides.ProviderOptionsJson ?? defaults.ProviderOptionsJson;
        defaults.OverrideClient = overrides.OverrideClient ?? defaults.OverrideClient;
        defaults.AdditionalProperties = MergeAdditionalProperties(defaults.AdditionalProperties, overrides.AdditionalProperties);
        return defaults;
    }

    private static Dictionary<string, object>? MergeAdditionalProperties(
        Dictionary<string, object>? defaults,
        Dictionary<string, object>? overrides)
    {
        if (defaults == null && overrides == null)
            return null;

        var merged = defaults == null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(defaults);

        if (overrides != null)
        {
            foreach (var entry in overrides)
                merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    private static bool HasAudioInputMode(AudioIOMode mode) =>
        mode is AudioIOMode.AudioToText
            or AudioIOMode.AudioToAudio
            or AudioIOMode.AudioToAudioAndText;

    private static bool HasAudioOutputMode(AudioIOMode mode) =>
        mode is AudioIOMode.TextToAudio
            or AudioIOMode.AudioToAudio
            or AudioIOMode.AudioToAudioAndText
            or AudioIOMode.TextToAudioAndText;

    private static bool HasTextOutputMode(AudioIOMode mode) =>
        mode is AudioIOMode.AudioToText
            or AudioIOMode.AudioToAudioAndText
            or AudioIOMode.TextToAudioAndText;

    private static ITextToSpeechClient WrapTextToSpeechClient(
        ITextToSpeechClient client,
        AudioConfig config,
        IServiceProvider? services)
    {
        var diagnostics = config.Diagnostics;
        if (diagnostics is not { EnableLogging: true } &&
            diagnostics is not { EnableOpenTelemetry: true })
        {
            return client;
        }

        var builder = client.AsBuilder();

        if (diagnostics.EnableLogging &&
            services?.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggerFactory)
        {
            builder.UseLogging(loggerFactory);
        }

        if (diagnostics.EnableOpenTelemetry)
        {
            builder.UseOpenTelemetry(
                services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory,
                diagnostics.SourceName,
                c => c.EnableSensitiveData = diagnostics.CaptureSensitiveTelemetry);
        }

        return builder.Build(services);
    }

    private static void ApplyGlobalLanguage(AudioConfig effectiveConfig)
    {
        if (string.IsNullOrWhiteSpace(effectiveConfig.Language))
            return;

        if (effectiveConfig.Stt != null)
            effectiveConfig.Stt.Language ??= effectiveConfig.Language;

        if (effectiveConfig.Tts != null)
            effectiveConfig.Tts.Language ??= effectiveConfig.Language;
    }

    private static void ApplyClientFamilyConfigs(
        AudioConfig effectiveConfig,
        AgentConfig? agentConfig,
        AgentRunConfig? runConfig)
    {
        if (agentConfig == null)
            return;

        ApplyClientFamilyDefaults(effectiveConfig, agentConfig);
        ApplyRunClientFamilyOverrides(effectiveConfig, agentConfig, runConfig);
    }

    private static void ApplyClientFamilyDefaults(AudioConfig effectiveConfig, AgentConfig agentConfig)
    {
        ApplyTtsClientConfig(
            effectiveConfig,
            agentConfig.ResolveClientConfig(ProviderClientFamily.TextToSpeech),
            overwrite: false);
        ApplySttClientConfig(
            effectiveConfig,
            agentConfig.ResolveClientConfig(ProviderClientFamily.SpeechToText),
            overwrite: false);
        ApplyVadClientConfig(
            effectiveConfig,
            agentConfig.ResolveClientConfig(ProviderClientFamily.VoiceActivityDetection),
            overwrite: false);
        ApplyEotClientConfig(
            effectiveConfig,
            agentConfig.ResolveClientConfig(ProviderClientFamily.EndOfTurnDetection),
            overwrite: false);
    }

    private static void ApplyRunClientFamilyOverrides(
        AudioConfig effectiveConfig,
        AgentConfig agentConfig,
        AgentRunConfig? runConfig)
    {
        if (runConfig?.Clients == null)
            return;

        ApplyRunOverride(
            effectiveConfig,
            agentConfig,
            runConfig,
            ProviderClientFamily.TextToSpeech,
            ApplyTtsClientConfig);
        ApplyRunOverride(
            effectiveConfig,
            agentConfig,
            runConfig,
            ProviderClientFamily.SpeechToText,
            ApplySttClientConfig);
        ApplyRunOverride(
            effectiveConfig,
            agentConfig,
            runConfig,
            ProviderClientFamily.VoiceActivityDetection,
            ApplyVadClientConfig);
        ApplyRunOverride(
            effectiveConfig,
            agentConfig,
            runConfig,
            ProviderClientFamily.EndOfTurnDetection,
            ApplyEotClientConfig);
    }

    private static void ApplyRunOverride(
        AudioConfig effectiveConfig,
        AgentConfig agentConfig,
        AgentRunConfig runConfig,
        ProviderClientFamily family,
        Action<AudioConfig, ClientProviderConfig?, bool> apply)
    {
        if (runConfig.Clients?.GetFamilyConfig(family) == null)
            return;

        apply(effectiveConfig, agentConfig.ResolveClientConfig(family, runConfig.Clients), true);
    }

    private static void ApplyTtsClientConfig(AudioConfig config, ClientProviderConfig? clientConfig, bool overwrite)
    {
        if (clientConfig == null)
            return;

        config.Tts ??= new TtsConfig();
        ApplyProviderConfig(config.Tts, clientConfig, overwrite);
    }

    private static void ApplySttClientConfig(AudioConfig config, ClientProviderConfig? clientConfig, bool overwrite)
    {
        if (clientConfig == null)
            return;

        config.Stt ??= new SttConfig();
        ApplyProviderConfig(config.Stt, clientConfig, overwrite);
    }

    private static void ApplyVadClientConfig(AudioConfig config, ClientProviderConfig? clientConfig, bool overwrite)
    {
        if (clientConfig == null)
            return;

        config.Vad ??= new VadConfig();
        ApplyProviderConfig(config.Vad, clientConfig, overwrite, applyModel: false);
    }

    private static void ApplyEotClientConfig(AudioConfig config, ClientProviderConfig? clientConfig, bool overwrite)
    {
        if (clientConfig == null)
            return;

        config.Eot ??= new EotConfig();
        ApplyProviderConfig(config.Eot, clientConfig, overwrite, applyModel: false);
    }

    private static void ApplyProviderConfig(TtsConfig config, ClientProviderConfig clientConfig, bool overwrite)
    {
        if (overwrite || string.IsNullOrWhiteSpace(config.Provider))
            config.Provider = clientConfig.ProviderKey ?? config.Provider;
        if (overwrite || string.IsNullOrWhiteSpace(config.ModelId))
            config.ModelId = clientConfig.ModelName ?? config.ModelId;
        if (overwrite || string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
            config.ProviderOptionsJson = clientConfig.ProviderOptionsJson ?? config.ProviderOptionsJson;
        config.AdditionalProperties = MergeAdditionalProperties(config.AdditionalProperties, clientConfig.AdditionalProperties);
    }

    private static void ApplyProviderConfig(SttConfig config, ClientProviderConfig clientConfig, bool overwrite)
    {
        if (overwrite || string.IsNullOrWhiteSpace(config.Provider))
            config.Provider = clientConfig.ProviderKey ?? config.Provider;
        if (overwrite || string.IsNullOrWhiteSpace(config.ModelId))
            config.ModelId = clientConfig.ModelName ?? config.ModelId;
        if (overwrite || string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
            config.ProviderOptionsJson = clientConfig.ProviderOptionsJson ?? config.ProviderOptionsJson;
        config.AdditionalProperties = MergeAdditionalProperties(config.AdditionalProperties, clientConfig.AdditionalProperties);
    }

    private static void ApplyProviderConfig(VadConfig config, ClientProviderConfig clientConfig, bool overwrite, bool applyModel)
    {
        if (overwrite || string.IsNullOrWhiteSpace(config.Provider))
            config.Provider = clientConfig.ProviderKey ?? config.Provider;
        if (overwrite || string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
            config.ProviderOptionsJson = clientConfig.ProviderOptionsJson ?? config.ProviderOptionsJson;
    }

    private static void ApplyProviderConfig(EotConfig config, ClientProviderConfig clientConfig, bool overwrite, bool applyModel)
    {
        if (overwrite || string.IsNullOrWhiteSpace(config.Provider))
            config.Provider = clientConfig.ProviderKey ?? config.Provider;
        if (overwrite || string.IsNullOrWhiteSpace(config.ProviderOptionsJson))
            config.ProviderOptionsJson = clientConfig.ProviderOptionsJson ?? config.ProviderOptionsJson;
    }

    private void EnsureRuntimeProviders(
        AudioConfig effectiveConfig,
        IServiceProvider? services,
        AgentClientSet? clientSet,
        AgentRunConfig? runConfig)
    {
        var hasAudioInput = HasAudioInputMode(effectiveConfig.IOMode);
        var hasAudioOutput = HasAudioOutputMode(effectiveConfig.IOMode);

        var configuredSttClient = effectiveConfig.Stt?.OverrideClient ?? clientSet?.SpeechToText;
        if (SpeechRecognizer == null &&
            (configuredSttClient != null || effectiveConfig.Stt != null) &&
            hasAudioInput)
        {
            var sttClient = configuredSttClient;
            var disposeClient = false;
            if (sttClient == null)
            {
                if (effectiveConfig.Stt == null || string.IsNullOrWhiteSpace(effectiveConfig.Stt.Provider))
                    throw new InvalidOperationException("Speech-to-text requires either an explicit client override or a configured provider.");

                var provider = GetRequiredProvider<ISpeechToTextClientProvider>(effectiveConfig.Stt.Provider);
                sttClient = provider.CreateSpeechToTextClient(ToClientProviderConfig(effectiveConfig.Stt), services);
                disposeClient = true;
            }

            SpeechRecognizer = MeaiSpeechRecognizerFactory.Create(
                sttClient,
                new SpeechRecognitionCapabilities
                {
                    StreamingInput = false,
                    InterimResults = false,
                    PreflightResults = false,
                    FinalResults = true,
                    OfflineRecognize = true
                },
                effectiveConfig.Stt?.UseStreamingRecognition == true,
                effectiveConfig.Stt?.Provider,
                effectiveConfig.Stt?.ModelId,
                disposeClient);
        }

        var configuredTtsClient = effectiveConfig.Tts?.OverrideClient ?? clientSet?.TextToSpeech;
        if (TextToSpeechClient == null &&
            (configuredTtsClient != null || effectiveConfig.Tts != null) &&
            hasAudioOutput)
        {
            var ttsClient = configuredTtsClient;
            if (ttsClient == null)
            {
                if (effectiveConfig.Tts == null || string.IsNullOrWhiteSpace(effectiveConfig.Tts.Provider))
                    throw new InvalidOperationException("Text-to-speech requires either an explicit client override or a configured provider.");

                var provider = GetRequiredProvider<ITextToSpeechClientProvider>(effectiveConfig.Tts.Provider);
                ttsClient = provider.CreateTextToSpeechClient(ToClientProviderConfig(effectiveConfig.Tts), services);
            }

            TextToSpeechClient = WrapTextToSpeechClient(
                ttsClient,
                effectiveConfig,
                services);
        }

        var configuredVadFactory = runConfig?.OverrideVoiceActivityDetectorFactory ?? clientSet?.VoiceActivityDetectorFactory;
        if (Vad == null && (configuredVadFactory != null || effectiveConfig.Vad != null) && hasAudioInput)
        {
            var lifetimeContext = new ProviderComponentLifetimeContext(Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession);
            Vad = configuredVadFactory?.Invoke(lifetimeContext);
            if (Vad == null)
            {
                if (effectiveConfig.Vad == null || string.IsNullOrWhiteSpace(effectiveConfig.Vad.Provider))
                    throw new InvalidOperationException("Voice activity detection requires either an explicit detector factory override or a configured provider.");

                var provider = GetRequiredProvider<IVoiceActivityDetectorProvider>(effectiveConfig.Vad.Provider);
                Vad = provider.CreateVoiceActivityDetector(
                    ToClientProviderConfig(effectiveConfig.Vad),
                    lifetimeContext,
                    services);
            }
        }

        var configuredEotFactory = runConfig?.OverrideEndOfTurnDetectorFactory ?? clientSet?.EndOfTurnDetectorFactory;
        if (EotDetector == null && (configuredEotFactory != null || effectiveConfig.Eot != null) && hasAudioInput)
        {
            var lifetimeContext = new ProviderComponentLifetimeContext(Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession);
            EotDetector = configuredEotFactory?.Invoke(lifetimeContext);
            if (EotDetector == null)
            {
                if (effectiveConfig.Eot == null || string.IsNullOrWhiteSpace(effectiveConfig.Eot.Provider))
                    throw new InvalidOperationException("End-of-turn detection requires either an explicit detector factory override or a configured provider.");

                var provider = GetRequiredProvider<IEndOfTurnDetectorProvider>(effectiveConfig.Eot.Provider);
                EotDetector = provider.CreateEndOfTurnDetector(
                    ToClientProviderConfig(effectiveConfig.Eot),
                    lifetimeContext,
                    services);
            }
        }
    }

    private TProvider GetRequiredProvider<TProvider>(string providerKey)
        where TProvider : class, IProvider
    {
        if (ProviderRegistry == null)
            throw new InvalidOperationException($"Audio provider '{providerKey}' requires a unified provider registry.");

        return ProviderRegistry.GetRequiredProvider<TProvider>(providerKey);
    }

    private static ClientProviderConfig ToClientProviderConfig(TtsConfig config)
    {
        var additionalProperties = config.AdditionalProperties is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(config.AdditionalProperties);

        if (!string.IsNullOrWhiteSpace(config.Voice))
            additionalProperties["voice"] = config.Voice;
        if (!string.IsNullOrWhiteSpace(config.OutputFormat))
            additionalProperties["outputFormat"] = config.OutputFormat;
        if (config.Speed.HasValue)
            additionalProperties["speed"] = config.Speed.Value;

        return new ClientProviderConfig
        {
            ProviderKey = config.Provider,
            ModelName = config.ModelId ?? string.Empty,
            ProviderOptionsJson = config.ProviderOptionsJson,
            AdditionalProperties = additionalProperties
        };
    }

    private static ClientProviderConfig ToClientProviderConfig(SttConfig config) => new()
    {
        ProviderKey = config.Provider,
        ModelName = config.ModelId ?? string.Empty,
        ProviderOptionsJson = config.ProviderOptionsJson,
        AdditionalProperties = config.AdditionalProperties
    };

    private static ClientProviderConfig ToClientProviderConfig(VadConfig config) => new()
    {
        ProviderKey = config.Provider,
        ProviderOptionsJson = config.ProviderOptionsJson,
        AdditionalProperties = new Dictionary<string, object>
        {
            ["activationThreshold"] = config.ActivationThreshold,
            ["minSpeechDuration"] = config.MinSpeechDuration,
            ["minSilenceDuration"] = config.MinSilenceDuration,
            ["prefixPaddingDuration"] = config.PrefixPaddingDuration
        }
    };

    private static ClientProviderConfig ToClientProviderConfig(EotConfig config) => new()
    {
        ProviderKey = config.Provider,
        ProviderOptionsJson = config.ProviderOptionsJson
    };

    private async Task RunAudioInputLoopAsync(
        RuntimeHookContext context,
        StructEventSubscription<AudioInputFrame> frames,
        AudioConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            var framesBatch = new AudioInputFrame[64];
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = frames.TryReadBatch(framesBatch);
                if (count == 0)
                {
                    await Task.Yield();
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    var frame = framesBatch[i];
                    if (frame.Audio.IsEmpty && !frame.IsFinal)
                        continue;

                    AudioInputBuffer? completedBuffer = null;
                    var shouldInterrupt = false;
                    var timestamp = TimeSpan.FromTicks(frame.TimestampNs / 100);
                    lock (_runtimeInputLock)
                    {
                        var key = new AudioInputKey(frame.SessionId, frame.BranchId);
                        if (!_runtimeInputBuffers.TryGetValue(key, out var buffer))
                        {
                            buffer = new AudioInputBuffer(frame.SessionId, frame.BranchId, frame.MimeType);
                            _runtimeInputBuffers[key] = buffer;
                        }

                        if (!frame.Audio.IsEmpty)
                        {
                            buffer.Append(frame.Audio);
                            var vadResult = ProcessVadFrame(frame, timestamp);
                            if (vadResult.HasValue)
                                shouldInterrupt = ObserveVadResult(context, buffer, timestamp, vadResult.Value);
                        }

                        if (frame.IsFinal)
                            buffer.MarkFinalFrame();

                        if (frame.IsFinal || buffer.ShouldCommitFromVad)
                        {
                            completedBuffer = buffer;
                            _runtimeInputBuffers.Remove(key);
                        }
                    }

                    if (shouldInterrupt)
                        await InterruptForVadStartAsync(context, cancellationToken).ConfigureAwait(false);

                    if (completedBuffer != null)
                        await CommitAudioInputAsync(
                                context,
                                completedBuffer,
                                effectiveConfig,
                                completedBuffer.CommitReason,
                                completedBuffer.LastSilenceDuration,
                                cancellationToken)
                            .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private VadResult? ProcessVadFrame(AudioInputFrame frame, TimeSpan timestamp)
    {
        if (Vad == null || frame.Audio.IsEmpty)
            return null;

        try
        {
            return Vad.Process(new AudioFrame
            {
                Data = frame.Audio,
                SampleRate = 16000,
                Channels = 1,
                Timestamp = timestamp,
                Duration = TimeSpan.Zero
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VAD processing failed: {ex.Message}");
            return null;
        }
    }

    private static bool ObserveVadResult(
        RuntimeHookContext context,
        AudioInputBuffer buffer,
        TimeSpan timestamp,
        VadResult result)
    {
        var speechStarted = false;

        if ((result.State == VadState.Starting || result.State == VadState.Speaking) &&
            !buffer.IsSpeaking)
        {
            buffer.MarkSpeechStarted(timestamp);
            speechStarted = true;
            context.Emit(new VadStartOfSpeechEvent(timestamp, result.SpeechProbability)
            {
                Channel = EventChannel.Streaming
            });
        }

        if (buffer.IsSpeaking &&
            (result.State == VadState.Stopping || (!result.IsSpeaking && result.State == VadState.Quiet)))
        {
            buffer.MarkSpeechEnded(timestamp);
            context.Emit(new VadEndOfSpeechEvent(
                timestamp,
                buffer.SpeechDuration,
                result.SpeechProbability)
            {
                Channel = EventChannel.Streaming
            });
        }

        return speechStarted;
    }

    private async ValueTask InterruptForVadStartAsync(
        RuntimeHookContext context,
        CancellationToken cancellationToken)
    {
        if (!context.HasActiveRuntimeTurns &&
            !context.EventFlows.ActiveFlows.Any(static stream => !stream.IsInterrupted))
        {
            return;
        }

        context.Emit(new UserInterruptedEvent(null)
        {
            Channel = EventChannel.Control
        });

        await context.RunAsync(new InterruptionRequestEvent(
            eventFlowId: null,
            Reason: "vad_start_of_speech",
            Source: InterruptionSource.User), cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitAudioInputAsync(
        RuntimeHookContext context,
        AudioInputBuffer buffer,
        AudioConfig effectiveConfig,
        string detectionMethod,
        TimeSpan silenceDuration,
        CancellationToken cancellationToken)
    {
        if (SpeechRecognizer == null || buffer.Length == 0)
            return;

        var transcriptionId = Guid.NewGuid().ToString("N")[..8];
        var startedAt = DateTime.UtcNow;

        context.Emit(new TranscriptionDeltaEvent(transcriptionId, "", false, null)
        {
            Channel = EventChannel.Streaming
        });

        string? transcript;
        try
        {
            var turnController = new TurnController(CreateTurnControllerOptions(effectiveConfig, detectionMethod));
            var preemptiveCoordinator = CreatePreemptiveGenerationCoordinator(effectiveConfig);
            UserTurnReadyEvent? readyTurn = null;
            UserTurnCommittedEvent? committedTurn = null;

            await foreach (var recognitionEvent in RecognizeAudioAsync(
                    new DataContent(buffer.ToArray(), buffer.MimeType),
                    effectiveConfig.Stt,
                    context.RuntimeId,
                    buffer.SessionId,
                    buffer.BranchId,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                context.Emit(recognitionEvent);
                ObservePreemptiveRecognition(context, preemptiveCoordinator, recognitionEvent);

                if (recognitionEvent is SpeechRecognitionFinalEvent final &&
                    !string.IsNullOrWhiteSpace(final.Transcript.Text))
                {
                    context.Emit(new TranscriptionDeltaEvent(transcriptionId, final.Transcript.Text, true, final.Transcript.Confidence)
                    {
                        Channel = EventChannel.Streaming
                    });
                }

                foreach (var turnEvent in turnController.Process(recognitionEvent))
                {
                    context.Emit(turnEvent);

                    if (turnEvent is UserTurnReadyEvent ready)
                        readyTurn = ready;
                    else if (turnEvent is UserTurnCommittedEvent committed)
                    {
                        committedTurn = committed;
                        ObservePreemptiveCommit(context, preemptiveCoordinator, committed);
                    }
                }
            }

            if (committedTurn == null && readyTurn != null)
            {
                var endpointDueAt = readyTurn.Context.ObservedAt + readyTurn.Decision.Delay;
                foreach (var turnEvent in turnController.AdvanceEndpointing(endpointDueAt))
                {
                    context.Emit(turnEvent);
                    if (turnEvent is UserTurnCommittedEvent committed)
                    {
                        committedTurn = committed;
                        ObservePreemptiveCommit(context, preemptiveCoordinator, committed);
                    }
                }
            }

            transcript = committedTurn?.Transcript.Text;
            if (committedTurn == null && detectionMethod == AudioInputBuffer.FinalFrameCommitReason)
            {
                foreach (var turnEvent in turnController.ManualCommit(DateTimeOffset.UtcNow))
                {
                    context.Emit(turnEvent);
                    if (turnEvent is UserTurnCommittedEvent committed)
                    {
                        committedTurn = committed;
                        ObservePreemptiveCommit(context, preemptiveCoordinator, committed);
                    }
                }

                transcript = committedTurn?.Transcript.Text;
            }

        }
        catch (Exception ex)
        {
            context.Emit(new AudioPipelineMetricsEvent("error", "runtime_stt_error", 1, "count")
            {
                Channel = EventChannel.Streaming
            });
            System.Diagnostics.Debug.WriteLine($"Runtime STT error: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript))
            return;

        var sttDuration = DateTime.UtcNow - startedAt;
        context.Emit(new TranscriptionCompletedEvent(transcriptionId, transcript, sttDuration)
        {
            Channel = EventChannel.Synchronous
        });

        var eotProbability = EotDetector?.GetEndOfTurnProbability(transcript) ?? 1.0f;
        context.Emit(new EotDetectedEvent(transcript, eotProbability, silenceDuration, detectionMethod)
        {
            Channel = EventChannel.Synchronous
        });

        try
        {
            await context.RunAsync(new UserTextInputEvent(transcript)
            {
                SessionId = buffer.SessionId,
                BranchId = buffer.BranchId
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Runtime input commit skipped: {ex.Message}");
        }
    }

    //
    // MIDDLEWARE HOOKS
    //

    /// <summary>
    /// Creates runtime audio infrastructure and starts the live input loop.
    /// </summary>
    public Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        var effectiveConfig = GetEffectiveConfig(context.RunConfig?.Audio);
        ApplyClientFamilyConfigs(effectiveConfig, context.Config, context.RunConfig);
        effectiveConfig.Validate();

        ApplyGlobalLanguage(effectiveConfig);

        if (effectiveConfig.Disabled == true ||
            effectiveConfig.ProcessingMode == AudioProcessingMode.Realtime ||
            !HasAudioInputMode(effectiveConfig.IOMode))
        {
            return Task.CompletedTask;
        }

        EnsureRuntimeProviders(effectiveConfig, context.Services, context.ClientSet, context.RunConfig);

        var subscription = context.StructEvents.Route<AudioInputFrame>().Subscribe();
        context.RegisterDisposable(subscription);
        context.RegisterBackgroundTask(runtimeToken =>
            RunAudioInputLoopAsync(context, subscription, effectiveConfig, runtimeToken));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops accepting buffered runtime audio input.
    /// </summary>
    public Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
    {
        lock (_runtimeInputLock)
        {
            _runtimeInputBuffers.Clear();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Does not process chat-message audio attachments.
    /// </summary>
    /// <remarks>
    /// Live audio is handled by the runtime <see cref="AudioInputFrame"/> loop.
    /// Attachment transcription is owned by <see cref="AudioAttachmentTranscriptionMiddleware"/>.
    /// </remarks>
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private async IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAudioAsync(
        DataContent audioContent,
        SttConfig? sttConfig,
        string? runtimeId,
        string? sessionId,
        string? branchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (SpeechRecognizer == null)
            yield break;

        var audioData = audioContent.Data;
        if (audioData.IsEmpty)
            yield break;

        var options = new SpeechRecognitionOptions
        {
            Provider = sttConfig?.Provider,
            Model = sttConfig?.ModelId,
            Language = sttConfig?.Language,
            RuntimeId = runtimeId,
            SessionId = sessionId,
            BranchId = branchId,
            AudioMimeType = audioContent.MediaType,
            SampleRate = sttConfig?.SpeechSampleRate
        };

        await foreach (var recognitionEvent in SpeechRecognizer
            .RecognizeAsync(
                EnumerateSingleAudioFrame(audioData, audioContent.MediaType, sessionId, branchId, cancellationToken),
                options,
                cancellationToken)
            .ConfigureAwait(false))
        {
            yield return recognitionEvent;
        }
    }

    private static async IAsyncEnumerable<AudioInputFrame> EnumerateSingleAudioFrame(
        ReadOnlyMemory<byte> audioData,
        string? mediaType,
        string? sessionId,
        string? branchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new AudioInputFrame(
            SessionId: sessionId,
            BranchId: branchId,
            Audio: audioData,
            MimeType: string.IsNullOrWhiteSpace(mediaType) ? "audio/pcm" : mediaType,
            TimestampNs: 0,
            IsFinal: true);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private TurnControllerOptions CreateTurnControllerOptions(
        AudioConfig effectiveConfig,
        string detectionMethod)
    {
        var eotConfig = effectiveConfig.Eot ?? new EotConfig();
        return new TurnControllerOptions
        {
            Mode = EndpointingMode.Hybrid,
            MinEndpointingDelay = detectionMethod == AudioInputBuffer.FinalFrameCommitReason
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(eotConfig.MinEndpointingDelay),
            MaxEndpointingDelay = TimeSpan.FromSeconds(eotConfig.MaxEndpointingDelay),
            EotDetector = EotDetector
        };
    }

    private static PreemptiveGenerationCoordinator? CreatePreemptiveGenerationCoordinator(AudioConfig effectiveConfig) =>
        effectiveConfig.EnablePreemptiveGeneration == true
            ? new PreemptiveGenerationCoordinator(new PreemptiveGenerationOptions
            {
                ConfidenceThreshold = effectiveConfig.PreemptiveGenerationThreshold ?? 0.7f
            })
            : null;

    private static void ObservePreemptiveRecognition(
        RuntimeHookContext context,
        PreemptiveGenerationCoordinator? coordinator,
        SpeechRecognitionEvent recognitionEvent)
    {
        if (coordinator is null || recognitionEvent is not SpeechRecognitionPreflightEvent preflight)
            return;

        var started = coordinator.TryStart(preflight);
        if (started is not null)
            context.Emit(started);
    }

    private static void ObservePreemptiveCommit(
        RuntimeHookContext context,
        PreemptiveGenerationCoordinator? coordinator,
        UserTurnCommittedEvent committed)
    {
        if (coordinator is null)
            return;

        var decision = coordinator.EvaluateCommit(committed);
        if (decision.ReuseCandidate || decision.Candidate is null)
            return;

        context.Emit(new PreemptiveGenerationDiscardedEvent(
            decision.Candidate.GenerationId,
            decision.Reason)
        {
            RecognitionId = decision.Candidate.RecognitionId,
            UtteranceId = decision.Candidate.UtteranceId,
            TranscriptRevisionId = decision.Candidate.TranscriptRevisionId
        });
    }

    /// <summary>
    /// Intercepts LLM streaming to enable Quick Answer (TTS on first sentence).
    /// </summary>
    /// <remarks>
    /// <para><b>Streaming Architecture:</b></para>
    /// <para>
    /// This hook wraps the LLM streaming response to synthesize audio in real-time.
    /// Audio synthesis happens on sentence boundaries (Quick Answer) for low latency.
    /// </para>
    /// <para>
    /// Returns null if audio is disabled or not configured, allowing the pipeline
    /// to pass through the stream without interception.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<ChatResponseUpdate>? WrapModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveConfig = GetEffectiveConfig(request.RunConfig?.Audio);
        effectiveConfig.Validate();

        var effectiveTtsClient = effectiveConfig.Tts?.OverrideClient ?? TextToSpeechClient;

        if (!string.IsNullOrWhiteSpace(effectiveConfig.Language) && effectiveConfig.Tts != null)
            effectiveConfig.Tts.Language ??= effectiveConfig.Language;

        // Check if audio is disabled for this request
        if (effectiveConfig.Disabled == true)
            return null;

        if (effectiveConfig.ProcessingMode == AudioProcessingMode.Realtime)
            return null;

        // Pipeline mode: synthesize audio from text via TTS
        if (effectiveTtsClient == null || !HasAudioOutputMode(effectiveConfig.IOMode))
        {
            // Return null to pass through without interception
            return null;
        }

        // Delegate to implementation method
        return StreamWithTtsAsync(request, handler, effectiveTtsClient, effectiveConfig, ct);
    }

    /// <summary>
    /// Implementation of TTS streaming with Quick Answer support.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithTtsAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        ITextToSpeechClient ttsClient,
        AudioConfig effectiveConfig,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Create stream handle for interruption support (from Priority Streaming)
        var stream = request.EventFlows?.Create();
        var synthesisId = Guid.NewGuid().ToString("N")[..8];
        await using var outputSession = new SpeechOutputSession(
            speechId: synthesisId,
            streamId: stream?.EventFlowId ?? synthesisId,
            sessionId: request.Session?.Id,
            synthesisId: synthesisId);
        RegisterActiveOutputSession(outputSession);
        var outputEventPump = PumpSpeechOutputEventsAsync(outputSession, request.EventCoordinator, ct);
        var synthesisState = new SynthesisState();
        var modelText = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var pacer = new SentenceTtsPacer();

        // Resolve TTS settings (per-request overrides > middleware defaults)
        var ttsOptions = effectiveConfig.Tts?.ToOptions() ?? new TextToSpeechOptions();
        if (!string.IsNullOrWhiteSpace(effectiveConfig.Language))
        {
            ttsOptions.Language ??= effectiveConfig.Language;
        }

        var voice = ttsOptions.VoiceId;
        var model = ttsOptions.ModelId;
        var outputFormat = ttsOptions.AudioFormat ?? "audio/mpeg";

        // Initialize metrics if not already set by BeforeIterationAsync
        _turnMetrics ??= new TurnMetrics { TurnStartTime = DateTime.UtcNow };
        var ttsStartTime = DateTime.UtcNow;
        DateTime? firstAudioTime = null;

        // Start filler monitoring task
        _fillerCts = new CancellationTokenSource();
        _fillerTask = MonitorForFillerAsync(request.EventCoordinator, request.StructEvents, stream, synthesisId, _fillerCts.Token);
        Task? ttsPacingTask = null;

        try
        {
            request.EventCoordinator?.Emit(new SynthesisStartedEvent(synthesisId, model, voice)
            {
                Channel = EventChannel.Synchronous,
                EventFlowId = stream?.EventFlowId
            });

            ttsPacingTask = ProcessTtsPacingAsync(
                modelText.Reader.ReadAllAsync(ct),
                pacer,
                outputSession,
                request.EventCoordinator,
                request.StructEvents,
                stream,
                synthesisId,
                ttsClient,
                ttsOptions,
                synthesisState,
                effectiveConfig,
                firstAudio => firstAudioTime ??= firstAudio,
                ct);

            await foreach (var update in handler(request).WithCancellation(ct))
            {
                // Cancel filler as soon as first token arrives
                _fillerCts?.Cancel();
                // Extract text from update
                var text = ExtractText(update);
                if (text != null)
                    await modelText.Writer.WriteAsync(text, ct).ConfigureAwait(false);

                yield return update;
            }

            modelText.Writer.TryComplete();
            await ttsPacingTask.ConfigureAwait(false);
            ttsPacingTask = null;
        }
        finally
        {
            var wasInterrupted = false;
            try
            {
                modelText.Writer.TryComplete();
                if (ttsPacingTask != null)
                    await ttsPacingTask.ConfigureAwait(false);

                // Ensure filler task is cancelled and awaited
                _fillerCts?.Cancel();
                if (_fillerTask != null)
                    await _fillerTask;

                wasInterrupted = stream?.IsInterrupted ?? false;

                if (wasInterrupted)
                {
                    await outputSession.InterruptAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    if (outputSession.State.QueuedChunks > 0)
                        await outputSession.MarkPlaybackFinishedAsync(CancellationToken.None).ConfigureAwait(false);

                    await outputSession.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await outputEventPump.ConfigureAwait(false);
                CaptureOutputExperienceMetrics(outputSession.State);

                request.EventCoordinator?.Emit(new SynthesisCompletedEvent(synthesisId, wasInterrupted, synthesisState.ChunkIndex, synthesisState.ChunkIndex)
                {
                    Channel = EventChannel.Control,
                    EventFlowId = stream?.EventFlowId,
                    CanInterrupt = false
                });
                stream?.Complete();
            }
            finally
            {
                UnregisterActiveOutputSession(outputSession);
            }

            // Upload assembled TTS audio to /artifacts if content store is available and synthesis produced audio
            if (synthesisState.AssembledAudio.Count > 0)
            {
                var sessionId = request.Session?.Id;
                var contentStore = request.ContentStore;
                if (contentStore != null && !string.IsNullOrEmpty(sessionId))
                {
                    try
                    {
                        var audioBytes = synthesisState.AssembledAudio.ToArray();
                        await contentStore.WriteBytesAsync(
                            scope: sessionId,
                            data: audioBytes,
                            metadata: new ContentMetadata
                            {
                                ContentType = outputFormat,
                                Origin = ContentSource.Agent,
                                Tags = new Dictionary<string, string>
                                {
                                    ["folder"]       = "/artifacts",
                                    ["audio-role"]   = "tts",
                                    ["synthesis-id"] = synthesisId,
                                    ["voice"]        = voice ?? "",
                                    ["model"]        = model ?? "",
                                    ["interrupted"]  = wasInterrupted ? "true" : "false"
                                }
                            },
                            options: new ContentWriteOptions { Mode = ContentWriteMode.Create });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TTS artifact upload failed: {ex.Message}");
                    }
                }
            }

            // Update and emit metrics
            var ttsEndTime = DateTime.UtcNow;
            _turnMetrics.TtsDuration = ttsEndTime - ttsStartTime;
            _turnMetrics.WasInterrupted = wasInterrupted;
            _turnMetrics.TotalChunks = synthesisState.ChunkIndex;
            _turnMetrics.DeliveredChunks = synthesisState.ChunkIndex;

            if (firstAudioTime.HasValue)
            {
                _turnMetrics.TimeToFirstAudio = firstAudioTime.Value - _turnMetrics.TurnStartTime;
            }

            EmitTurnMetrics(request.EventCoordinator);
        }
    }

    /// <summary>
    /// Emits metrics for the completed audio turn.
    /// </summary>
    private void EmitTurnMetrics(IEventCoordinator? eventCoordinator)
    {
        if (_turnMetrics == null)
            return;

        var totalLatency = DateTime.UtcNow - _turnMetrics.TurnStartTime;

        // Emit individual metrics
        if (_turnMetrics.SttDuration.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "stt_duration", _turnMetrics.SttDuration.Value.TotalMilliseconds, "ms")
            {
                Channel = EventChannel.Streaming
            });
        }

        if (_turnMetrics.TtsDuration.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "tts_duration", _turnMetrics.TtsDuration.Value.TotalMilliseconds, "ms")
            {
                Channel = EventChannel.Streaming
            });
        }

        if (_turnMetrics.TimeToFirstAudio.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "time_to_first_audio", _turnMetrics.TimeToFirstAudio.Value.TotalMilliseconds, "ms")
            {
                Channel = EventChannel.Streaming
            });
        }

        eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "total_latency", totalLatency.TotalMilliseconds, "ms")
        {
            Channel = EventChannel.Streaming
        });

        if (_turnMetrics.UserWpm.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("quality", "user_wpm", _turnMetrics.UserWpm.Value, "wpm")
            {
                Channel = EventChannel.Streaming
            });
        }

        if (_turnMetrics.WasInterrupted)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("quality", "was_interrupted", 1, "bool")
            {
                Channel = EventChannel.Streaming
            });
        }

        eventCoordinator?.Emit(new AudioPipelineMetricsEvent("throughput", "total_chunks", _turnMetrics.TotalChunks, "chunks")
        {
            Channel = EventChannel.Streaming
        });

        EmitAudioExperienceMetrics(eventCoordinator);

        // Reset metrics for next turn
        _turnMetrics = null;
    }

    private void CaptureOutputExperienceMetrics(SpeechOutputState outputState)
    {
        if (_turnMetrics == null)
            return;

        _turnMetrics.AudioGeneratedDuration = outputState.GeneratedDuration;
        _turnMetrics.AudioPlayedDuration = outputState.PlayedDuration;
        _turnMetrics.AudioDiscardedDuration = outputState.DiscardedDuration;
        _turnMetrics.PlaybackCompletionRatio = outputState.GeneratedDuration > TimeSpan.Zero
            ? Math.Clamp(outputState.PlayedDuration.TotalMilliseconds / outputState.GeneratedDuration.TotalMilliseconds, 0, 1)
            : outputState.QueuedChunks > 0 && !outputState.Interrupted
                ? 1.0
                : null;
    }

    private void EmitAudioExperienceMetrics(IEventCoordinator? eventCoordinator)
    {
        if (_turnMetrics == null)
            return;

        if (_turnMetrics.TimeToFirstAudio.HasValue)
            EmitAudioExperienceMetric(eventCoordinator, "time_to_first_audio", _turnMetrics.TimeToFirstAudio.Value.TotalMilliseconds, "ms");

        if (_turnMetrics.PlaybackCompletionRatio.HasValue)
            EmitAudioExperienceMetric(eventCoordinator, "playback_completion_ratio", _turnMetrics.PlaybackCompletionRatio.Value, "ratio");

        if (_turnMetrics.AudioGeneratedDuration.HasValue)
            EmitAudioExperienceMetric(eventCoordinator, "audio_generated_duration", _turnMetrics.AudioGeneratedDuration.Value.TotalMilliseconds, "ms");

        if (_turnMetrics.AudioPlayedDuration.HasValue)
            EmitAudioExperienceMetric(eventCoordinator, "audio_played_duration", _turnMetrics.AudioPlayedDuration.Value.TotalMilliseconds, "ms");

        if (_turnMetrics.AudioDiscardedDuration.HasValue)
            EmitAudioExperienceMetric(eventCoordinator, "audio_discarded_duration", _turnMetrics.AudioDiscardedDuration.Value.TotalMilliseconds, "ms");
    }

    private static void EmitAudioExperienceMetric(
        IEventCoordinator? eventCoordinator,
        string metricName,
        double value,
        string? unit)
    {
        eventCoordinator?.Emit(new AudioExperienceMetricEvent(metricName, value, unit));
    }

    //
    // VAD INTERRUPT HANDLING (uses core IEventFlowRegistry)
    //

    private void RegisterActiveOutputSession(ISpeechOutputSession outputSession)
    {
        lock (_interruptionLock)
        {
            _activeOutputSessions[outputSession.StreamId] = outputSession;
            _interruptionController ??= new InterruptionController(CreateInterruptionControllerOptions());
        }
    }

    private void UnregisterActiveOutputSession(ISpeechOutputSession outputSession)
    {
        lock (_interruptionLock)
        {
            _activeOutputSessions.Remove(outputSession.StreamId);
            if (_activeOutputSessions.Count == 0)
                _interruptionController = null;
        }
    }

    private void ObserveSpeechOutputForInterruption(SpeechOutputEvent evt)
    {
        lock (_interruptionLock)
        {
            _interruptionController ??= new InterruptionController(CreateInterruptionControllerOptions());
            _interruptionController.Process(evt);
        }
    }

    internal void OnVadStartOfSpeech(HookContext context, string? transcribedText)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var activeStream = TryGetFirstActiveStream(context);
        InterruptionDecision decision;
        lock (_interruptionLock)
        {
            _interruptionController ??= new InterruptionController(CreateInterruptionControllerOptions());

            if (activeStream is not null && !_activeOutputSessions.ContainsKey(activeStream.EventFlowId))
                _interruptionController.Process(CreatePlaybackStartedForInterruption(activeStream, observedAt));

            decision = string.IsNullOrWhiteSpace(transcribedText)
                ? _interruptionController.Process(new SpeechRecognitionStartedEvent
                {
                    Context = CreateRecognitionContextForInterruption(observedAt)
                })
                : _interruptionController.Process(new SpeechRecognitionFinalEvent
                {
                    Context = CreateRecognitionContextForInterruption(observedAt),
                    Transcript = new SpeechRecognitionTranscript(transcribedText, TranscriptRevisionId: Guid.NewGuid().ToString("N"))
                });
        }

        ApplyInterruptionDecision(context, activeStream, decision, transcribedText);
    }

    private async Task ResumeIfStillPausedAsync(HookContext context)
    {
        PausedSynthesisState? stateToResume;

        lock (_pauseLock)
        {
            if (_pausedSynthesis == null || !ResumeFalseInterruption)
                return;

            stateToResume = _pausedSynthesis;
            _pausedSynthesis = null;
        }

        var pauseDuration = DateTime.UtcNow - stateToResume.PausedAt;

        context.Emit(new SpeechResumedEvent(
            stateToResume.SynthesisId,
            pauseDuration)
        {
            Channel = EventChannel.Control,
            EventFlowId = stateToResume.SynthesisId
        });

        // Flush buffered chunks
        foreach (var chunk in stateToResume.BufferedChunks)
        {
            context.Emit(chunk);
        }

        // Emit metrics
        context.Emit(new AudioPipelineMetricsEvent(
            "quality",
            "false_interruption_recovered",
            pauseDuration.TotalMilliseconds,
            "ms")
        {
            Channel = EventChannel.Streaming
        });

        context.Emit(new AudioExperienceMetricEvent(
            "false_interruption_rate",
            1,
            "count",
            SpeechId: stateToResume.OutputSession?.SpeechId,
            OutputStreamId: stateToResume.EventFlow.EventFlowId)
        {
            Channel = EventChannel.Streaming
        });

        context.Emit(new AudioExperienceMetricEvent(
            "resume_false_interruption_rate",
            1,
            "count",
            SpeechId: stateToResume.OutputSession?.SpeechId,
            OutputStreamId: stateToResume.EventFlow.EventFlowId)
        {
            Channel = EventChannel.Streaming
        });

        if (stateToResume.OutputSession is not null)
            await stateToResume.OutputSession.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void ApplyInterruptionDecision(
        HookContext context,
        IEventFlowHandle? activeStream,
        InterruptionDecision decision,
        string? transcribedText)
    {
        switch (decision.Action)
        {
            case InterruptionAction.PauseOutput:
                if (activeStream is not null)
                {
                    var outputSession = TryGetActiveOutputSession(activeStream.EventFlowId);
                    if (outputSession is not null)
                        _ = outputSession.PauseAsync(CancellationToken.None).AsTask();

                    PauseForPotentialInterruption(context, activeStream, outputSession, decision.Reason);
                }
                else
                {
                    ConfirmInterruption(context, transcribedText, null);
                }
                break;

            case InterruptionAction.ResumeOutput:
                _ = ResumeIfStillPausedAsync(context);
                break;

            case InterruptionAction.InterruptOutput:
                ISpeechOutputSession? interruptedOutput = null;
                if (activeStream is not null &&
                    TryGetActiveOutputSession(activeStream.EventFlowId) is { } interruptibleOutput)
                {
                    interruptedOutput = interruptibleOutput;
                    _ = interruptibleOutput.InterruptAsync(CancellationToken.None).AsTask();
                }

                ConfirmInterruption(context, transcribedText, interruptedOutput);
                break;
        }
    }

    private ISpeechOutputSession? TryGetActiveOutputSession(string streamId)
    {
        lock (_interruptionLock)
        {
            return _activeOutputSessions.GetValueOrDefault(streamId);
        }
    }

    private void PauseForPotentialInterruption(
        HookContext context,
        IEventFlowHandle activeStream,
        ISpeechOutputSession? outputSession,
        string reason)
    {
        lock (_pauseLock)
        {
            if (_pausedSynthesis is not null)
                return;

            _pausedSynthesis = new PausedSynthesisState
            {
                SynthesisId = activeStream.EventFlowId,
                EventFlow = activeStream,
                OutputSession = outputSession,
                PausedAt = DateTime.UtcNow
            };

            context.Emit(new SpeechPausedEvent(
                activeStream.EventFlowId,
                reason)
            {
                Channel = EventChannel.Control,
                EventFlowId = activeStream.EventFlowId
            });

            var resumeTimeoutCts = _pausedSynthesis.ResumeTimeoutCts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(FalseInterruptionTimeout),
                        resumeTimeoutCts.Token);

                    await ResumeIfStillPausedAsync(context);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled because speech resumed or was confirmed as an interruption.
                }
            });
        }
    }

    private void ConfirmInterruption(
        HookContext context,
        string? transcribedText,
        ISpeechOutputSession? outputSession)
    {
        PausedSynthesisState? pausedState;
        lock (_pauseLock)
        {
            // Cancel resume timer if running
            pausedState = _pausedSynthesis;
            pausedState?.ResumeTimeoutCts.Cancel();
            _pausedSynthesis = null;
        }

        // Interrupt all active audio streams
        context.EventFlows?.InterruptAll();

        context.Emit(new UserInterruptedEvent(transcribedText)
        {
            Channel = EventChannel.Control
        });

        var bargeInStopLatency = pausedState is not null
            ? DateTime.UtcNow - pausedState.PausedAt
            : TimeSpan.Zero;

        context.Emit(new AudioExperienceMetricEvent(
            "barge_in_stop_latency",
            Math.Max(0, bargeInStopLatency.TotalMilliseconds),
            "ms",
            SpeechId: outputSession?.SpeechId ?? pausedState?.OutputSession?.SpeechId,
            OutputStreamId: outputSession?.StreamId ?? pausedState?.EventFlow.EventFlowId)
        {
            Channel = EventChannel.Streaming
        });
    }

    private InterruptionControllerOptions CreateInterruptionControllerOptions() =>
        new()
        {
            BackchannelStrategy = BackchannelStrategy,
            MinWordsForInterruption = MinWordsForInterruption,
            EnableFalseInterruptionRecovery = EnableFalseInterruptionRecovery,
            FalseInterruptionTimeout = TimeSpan.FromSeconds(FalseInterruptionTimeout),
            ResumeFalseInterruption = ResumeFalseInterruption
        };

    private static IEventFlowHandle? TryGetFirstActiveStream(HookContext context)
    {
        if (context.EventFlows is not { } streams)
            return null;

        foreach (var handle in streams.ActiveFlows)
        {
            if (!handle.IsInterrupted)
                return handle;
        }

        return null;
    }

    private static SpeechOutputPlaybackStartedEvent CreatePlaybackStartedForInterruption(
        IEventFlowHandle stream,
        DateTimeOffset observedAt) =>
        new()
        {
            Context = new SpeechOutputContext(
                RuntimeId: null,
                SessionId: null,
                BranchId: null,
                SpeechId: stream.EventFlowId,
                StreamId: stream.EventFlowId,
                OutputId: stream.EventFlowId,
                Provider: null,
                Model: null,
                Voice: null,
                SequenceNumber: null,
                TimestampNs: null,
                ObservedAt: observedAt),
            State = new SpeechOutputState()
        };

    private static SpeechRecognitionContext CreateRecognitionContextForInterruption(DateTimeOffset observedAt) =>
        new(
            RuntimeId: null,
            SessionId: null,
            BranchId: null,
            UtteranceId: Guid.NewGuid().ToString("N"),
            RecognitionId: Guid.NewGuid().ToString("N"),
            SegmentId: null,
            ProviderRequestId: null,
            Provider: null,
            Model: null,
            SequenceNumber: null,
            TimestampNs: null,
            ObservedAt: observedAt);

    //
    // SPEED ADAPTATION (built into middleware)
    //

    internal void UpdateSpeedEstimate(float? wordsPerMinute, int wordCount)
    {
        if (!EnableSpeedAdaptation || !wordsPerMinute.HasValue || wordCount == 0)
            return;

        // Weighted learning rate based on utterance length
        var weight = Math.Min(1f, 0.1f * (wordCount + 3f) / 8f);
        _currentWpm = _currentWpm * (1 - weight) + wordsPerMinute.Value * weight;
    }

    internal float AdjustEndpointingDelay(float baseDelay)
    {
        if (!EnableSpeedAdaptation) return baseDelay;

        // Fast speakers get shorter delays
        var speedCoefficient = _currentWpm / 150f;
        return baseDelay / speedCoefficient;
    }

    //
    // END-OF-TURN DETECTION (hybrid strategy)
    //

    internal float CalculateEndpointingDelay(string text, float silenceDuration)
        => CalculateEndpointingDelay(text, silenceDuration, _config.Eot ?? new EotConfig());

    internal float CalculateEndpointingDelay(string text, float silenceDuration, EotConfig eot)
    {
        // Fast path: long silence = definitely done (no ML needed)
        if (eot.SilenceStrategy == EotDetectionStrategy.FastPath &&
            silenceDuration >= eot.SilenceFastPathThreshold)
        {
            return 0; // Respond immediately
        }

        // Detector path: get completion probability
        float detectorProbability = 0.5f;
        if (EotDetector != null && eot.DetectorStrategy != EotDetectionStrategy.Disabled)
        {
            detectorProbability = EotDetector.GetEndOfTurnProbability(text);
        }

        // Combine detector probability with silence duration
        // Longer silence → boost probability confidence
        float silenceBoost = eot.SilenceFastPathThreshold <= 0
            ? 1.0f
            : Math.Min(silenceDuration / eot.SilenceFastPathThreshold, 1.0f);
        float combinedProbability = eot.UseCombinedProbability
            ? Math.Max(detectorProbability, silenceBoost * eot.SilenceBoostMultiplier)
            : detectorProbability;

        // Interpolate delay based on combined probability
        var baseDelay = combinedProbability > 0.7f
            ? eot.MinEndpointingDelay
            : eot.MaxEndpointingDelay - (combinedProbability * (eot.MaxEndpointingDelay - eot.MinEndpointingDelay));

        return AdjustEndpointingDelay(baseDelay);
    }

    //
    // TEXT FILTERING
    //

    internal string FilterTextForTts(string text)
        => FilterTextForTts(text, _config);

    private static string FilterTextForTts(string text, AudioConfig config)
    {
        if (config.EnableTextFiltering == false)
            return text;

        var filtered = text;

        // Remove code blocks
        if (config.FilterCodeBlocks ?? true)
        {
            filtered = CodeBlockRegex().Replace(filtered, " [code omitted] ");
        }

        // Remove tables
        if (config.FilterTables ?? true)
        {
            filtered = TableRegex().Replace(filtered, " [table omitted] ");
        }

        // Remove URLs (keep domain for context)
        if (config.FilterUrls ?? true)
        {
            filtered = UrlRegex().Replace(filtered, m =>
            {
                try
                {
                    var uri = new Uri(m.Value);
                    return $" {uri.Host} ";
                }
                catch
                {
                    return " [link] ";
                }
            });
        }

        // Remove markdown formatting
        if (config.FilterMarkdownFormatting ?? true)
        {
            // Bold
            filtered = BoldRegex().Replace(filtered, "$1");
            // Italic
            filtered = ItalicRegex().Replace(filtered, "$1");
            // Strikethrough
            filtered = StrikethroughRegex().Replace(filtered, "$1");
            // Inline code
            filtered = InlineCodeRegex().Replace(filtered, "$1");
            // Headers
            filtered = HeaderRegex().Replace(filtered, "$1");
        }

        // Remove emoji
        if (config.FilterEmoji ?? true)
        {
            filtered = EmojiRegex().Replace(filtered, "");
        }

        // Clean up multiple spaces
        filtered = MultipleSpacesRegex().Replace(filtered, " ").Trim();

        return filtered;
    }

    //
    // FILLER AUDIO
    //

    /// <summary>
    /// Pre-synthesize filler phrases at startup for instant playback.
    /// Call this once during agent initialization.
    /// </summary>
    public async Task PreCacheFillerAudioAsync(CancellationToken ct = default)
    {
        if (!EnableFillerAudio || TextToSpeechClient == null)
            return;

        _cachedFillers = new List<CachedFillerAudio>();

        foreach (var phrase in FillerPhrases)
        {
            try
            {
                var response = await TextToSpeechClient.GetAudioAsync(
                    phrase,
                    new TextToSpeechOptions
                    {
                        VoiceId = _config.FillerVoice ?? DefaultVoice,
                        ModelId = DefaultModel,
                        Speed = _config.FillerSpeed ?? 0.95f
                    },
                    ct);

                var audio = ExtractAudioData(response.Contents);
                _cachedFillers.Add(new CachedFillerAudio
                {
                    Phrase = phrase,
                    AudioData = audio?.Data.ToArray() ?? [],
                    MimeType = audio?.MediaType ?? "audio/mpeg",
                    Duration = TimeSpan.Zero
                });
            }
            catch (Exception ex)
            {
                // Log but don't fail - filler audio is optional
                System.Diagnostics.Debug.WriteLine($"Failed to cache filler '{phrase}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Monitors LLM generation and plays filler audio if threshold exceeded.
    /// </summary>
    private async Task MonitorForFillerAsync(
        IEventCoordinator? eventCoordinator,
        IStructEventHub? structEvents,
        IEventFlowHandle? stream,
        string synthesisId,
        CancellationToken ct)
    {
        if (!EnableFillerAudio || _cachedFillers == null || _cachedFillers.Count == 0)
            return;

        try
        {
            // Wait for silence threshold
            await Task.Delay(TimeSpan.FromSeconds(FillerSilenceThreshold), ct);

            // If we're still waiting for LLM, play filler
            if (!ct.IsCancellationRequested)
            {
                var filler = _cachedFillers[Random.Shared.Next(_cachedFillers.Count)];
                var fillerChunk = new AudioChunkEvent(
                    synthesisId,
                    Convert.ToBase64String(filler.AudioData),
                    filler.MimeType,
                    -1, // Negative index indicates filler
                    filler.Duration,
                    true)
                {
                    Channel = EventChannel.Streaming,
                    EventFlowId = stream?.EventFlowId,
                    CanInterrupt = true
                };

                eventCoordinator?.Emit(fillerChunk);
                EmitAudioOutputFrame(
                    structEvents,
                    fillerChunk,
                    filler.AudioData);

                eventCoordinator?.Emit(new FillerAudioPlayedEvent(
                    filler.Phrase,
                    filler.Duration)
                {
                    Channel = EventChannel.Streaming
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Normal - LLM responded before threshold
        }
    }

    //
    // HELPERS
    //

    /// <summary>
    /// State for tracking synthesis progress and accumulating audio bytes across async enumeration.
    /// </summary>
    private sealed class SynthesisState
    {
        public int ChunkIndex { get; set; }

        /// <summary>Accumulated raw audio bytes across all chunks for artifact upload.</summary>
        public List<byte> AssembledAudio { get; } = [];
    }

    private async IAsyncEnumerable<AudioChunkEvent> SynthesizeAndEmitAsync(
        IEventCoordinator? eventCoordinator,
        IStructEventHub? structEvents,
        ISpeechOutputSession outputSession,
        string text,
        IEventFlowHandle? stream,
        string synthesisId,
        ITextToSpeechClient ttsClient,
        TextToSpeechOptions options,
        SynthesisState state,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (stream?.IsInterrupted == true) yield break;
        var audioFrameEmitter = CreateAudioOutputFrameEmitter(structEvents);

        await foreach (var chunk in ttsClient.GetStreamingAudioAsync(text, options, ct))
        {
            if (stream?.IsInterrupted == true) break;

            var audioContent = ExtractAudioData(chunk.Contents);
            var audioData = audioContent?.Data ?? ReadOnlyMemory<byte>.Empty;
            var audioBytes = audioData.ToArray();

            // Accumulate bytes for artifact upload after synthesis completes
            state.AssembledAudio.AddRange(audioBytes);

            var audioChunk = new AudioChunkEvent(
                synthesisId,
                Convert.ToBase64String(audioBytes),
                audioContent?.MediaType ?? options.AudioFormat ?? "audio/mpeg",
                state.ChunkIndex++,
                TimeSpan.Zero,
                chunk.Kind == TextToSpeechResponseUpdateKind.AudioUpdated ||
                chunk.Kind == TextToSpeechResponseUpdateKind.SessionClose)
            {
                Channel = EventChannel.Streaming,
                EventFlowId = stream?.EventFlowId,
                CanInterrupt = true
            };

            var frame = CreateAudioOutputFrame(audioChunk, audioBytes);
            await outputSession.PushAudioAsync(frame, ct).ConfigureAwait(false);
            eventCoordinator?.Emit(audioChunk);
            EmitAudioOutputFrame(audioFrameEmitter, frame);
            if (!outputSession.State.IsPaused)
            {
                var playedDuration = outputSession.State.PlayedDuration + frame.Duration;
                await outputSession.MarkPlaybackProgressAsync(
                    playedDuration,
                    playedDuration,
                    ct).ConfigureAwait(false);
            }
            yield return audioChunk;
        }
    }

    private async Task ProcessTtsPacingAsync(
        IAsyncEnumerable<string> modelText,
        ITtsPacer pacer,
        ISpeechOutputSession outputSession,
        IEventCoordinator? eventCoordinator,
        IStructEventHub? structEvents,
        IEventFlowHandle? stream,
        string synthesisId,
        ITextToSpeechClient ttsClient,
        TextToSpeechOptions ttsOptions,
        SynthesisState synthesisState,
        AudioConfig effectiveConfig,
        Action<DateTime> onFirstAudio,
        CancellationToken cancellationToken)
    {
        var pacingOptions = new TtsPacingOptions
        {
            EnableQuickAnswer = effectiveConfig.EnableQuickAnswer ?? true,
            TextFilter = text => FilterTextForTts(text, effectiveConfig)
        };

        await foreach (var segment in pacer
            .SegmentAsync(modelText, outputSession.State, pacingOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            await outputSession.PushTextAsync(segment.Text, cancellationToken).ConfigureAwait(false);
            await foreach (var _ in SynthesizeAndEmitAsync(
                eventCoordinator,
                structEvents,
                outputSession,
                segment.Text,
                stream,
                synthesisId,
                ttsClient,
                ttsOptions,
                synthesisState,
                cancellationToken).ConfigureAwait(false))
            {
                onFirstAudio(DateTime.UtcNow);
            }
        }
    }

    private static DataContent? ExtractAudioData(IEnumerable<AIContent> contents)
    {
        foreach (var content in contents)
        {
            if (content is DataContent data &&
                data.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return data;
            }
        }

        return null;
    }

    private async Task PumpSpeechOutputEventsAsync(
        ISpeechOutputSession outputSession,
        IEventCoordinator? eventCoordinator,
        CancellationToken cancellationToken)
    {
        await foreach (var evt in outputSession.Events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            ObserveSpeechOutputForInterruption(evt);
            eventCoordinator?.Emit(evt);
        }
    }

    private static SequencedStructEventEmitter<AudioOutputFrame>? CreateAudioOutputFrameEmitter(IStructEventHub? structEvents) =>
        structEvents?.Route<AudioOutputFrame>().CreateSequencedEmitter();

    private static void EmitAudioOutputFrame(
        SequencedStructEventEmitter<AudioOutputFrame>? emitter,
        AudioOutputFrame frame)
    {
        if (emitter is not { } audioFrames)
            return;

        audioFrames.Emit(frame);
    }

    private static void EmitAudioOutputFrame(
        SequencedStructEventEmitter<AudioOutputFrame>? emitter,
        AudioChunkEvent audioChunk,
        ReadOnlyMemory<byte> audioBytes) =>
        EmitAudioOutputFrame(emitter, CreateAudioOutputFrame(audioChunk, audioBytes));

    private static void EmitAudioOutputFrame(
        IStructEventHub? structEvents,
        AudioChunkEvent audioChunk,
        ReadOnlyMemory<byte> audioBytes)
    {
        var emitter = CreateAudioOutputFrameEmitter(structEvents);
        EmitAudioOutputFrame(emitter, CreateAudioOutputFrame(audioChunk, audioBytes));
    }

    private static AudioOutputFrame CreateAudioOutputFrame(
        AudioChunkEvent audioChunk,
        ReadOnlyMemory<byte> audioBytes) =>
        new(
            audioChunk.SynthesisId,
            audioBytes,
            audioChunk.MimeType,
            audioChunk.ChunkIndex,
            audioChunk.Duration,
            audioChunk.IsLast,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static bool IsKnownBackchannel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var cleaned = text.Trim().ToLowerInvariant();
        return BackchannelPatterns.Any(p => p.IsMatch(cleaned));
    }

    private static readonly Regex[] BackchannelPatterns =
    [
        BackchannelMhmRegex(),
        BackchannelFillerRegex(),
        BackchannelAffirmativeRegex(),
    ];

    private static string? ExtractText(ChatResponseUpdate update)
    {
        // Extract text content from ChatResponseUpdate
        return update.Contents?.OfType<TextContent>().FirstOrDefault()?.Text;
    }

    //
    // COMPILED REGEX PATTERNS
    //

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Compiled)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"\|[^\n]+\|(\n\|[^\n]+\|)+", RegexOptions.Compiled)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.Compiled)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*([^*]+)\*", RegexOptions.Compiled)]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~([^~]+)~~", RegexOptions.Compiled)]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"`([^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^#{1,6}\s*(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"[\p{So}\p{Cs}]+", RegexOptions.Compiled)]
    private static partial Regex EmojiRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"^m+-?hm+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BackchannelMhmRegex();

    [GeneratedRegex(@"^(um+|uh+|oh+|ah+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BackchannelFillerRegex();

    [GeneratedRegex(@"^(yes|sure|right|really|okay|yeah+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BackchannelAffirmativeRegex();

    //
    // INTERNAL TYPES
    //
    // FILLER AUDIO HELPERS
    //

    /// <summary>
    /// Cached pre-synthesized filler audio for instant playback.
    /// </summary>
    private sealed class CachedFillerAudio
    {
        public required string Phrase { get; init; }
        public required byte[] AudioData { get; init; }
        public required string MimeType { get; init; }
        public required TimeSpan Duration { get; init; }
    }

    //
    // FALSE INTERRUPTION RECOVERY HELPERS
    //

    /// <summary>
    /// State for tracking paused synthesis during false interruption detection.
    /// </summary>
    private sealed class PausedSynthesisState
    {
        public required string SynthesisId { get; init; }
        public required IEventFlowHandle EventFlow { get; init; }
        public ISpeechOutputSession? OutputSession { get; init; }
        public required DateTime PausedAt { get; init; }
        public Queue<AudioChunkEvent> BufferedChunks { get; } = new();
        public CancellationTokenSource ResumeTimeoutCts { get; init; } = new();
    }

    //

    private readonly record struct AudioInputKey(string? SessionId, string? BranchId);

    private sealed class AudioInputBuffer(string? sessionId, string? branchId, string mimeType)
    {
        private readonly MemoryStream _audio = new();
        public const string VadEndCommitReason = "vad-end-of-speech";
        public const string FinalFrameCommitReason = "final-frame";
        private TimeSpan? _speechStartedAt;

        public string? SessionId { get; } = sessionId;
        public string? BranchId { get; } = branchId;
        public string MimeType { get; } = string.IsNullOrWhiteSpace(mimeType)
            ? "audio/pcm"
            : mimeType;
        public long Length => _audio.Length;
        public bool IsSpeaking { get; private set; }
        public bool ShouldCommitFromVad { get; private set; }
        public string CommitReason { get; private set; } = FinalFrameCommitReason;
        public TimeSpan SpeechDuration { get; private set; }
        public TimeSpan LastSilenceDuration { get; private set; }

        public void Append(ReadOnlyMemory<byte> audio)
        {
            if (!audio.IsEmpty)
                _audio.Write(audio.Span);
        }

        public void MarkSpeechStarted(TimeSpan timestamp)
        {
            IsSpeaking = true;
            ShouldCommitFromVad = false;
            _speechStartedAt = timestamp;
            SpeechDuration = TimeSpan.Zero;
            LastSilenceDuration = TimeSpan.Zero;
            CommitReason = VadEndCommitReason;
        }

        public void MarkSpeechEnded(TimeSpan timestamp)
        {
            if (_speechStartedAt.HasValue && timestamp >= _speechStartedAt.Value)
                SpeechDuration = timestamp - _speechStartedAt.Value;

            IsSpeaking = false;
            ShouldCommitFromVad = true;
            LastSilenceDuration = TimeSpan.Zero;
            CommitReason = VadEndCommitReason;
        }

        public void MarkFinalFrame()
        {
            CommitReason = FinalFrameCommitReason;
            if (IsSpeaking)
                IsSpeaking = false;
        }

        public byte[] ToArray() => _audio.ToArray();
    }

    /// <summary>
    /// Tracks metrics for a single audio turn.
    /// </summary>
    private sealed class TurnMetrics
    {
        public DateTime TurnStartTime { get; set; }
        public TimeSpan? SttDuration { get; set; }
        public TimeSpan? TtsDuration { get; set; }
        public TimeSpan? TimeToFirstAudio { get; set; }
        public float? UserWpm { get; set; }
        public bool WasInterrupted { get; set; }
        public int TotalChunks { get; set; }
        public int DeliveredChunks { get; set; }
        public TimeSpan? AudioGeneratedDuration { get; set; }
        public TimeSpan? AudioPlayedDuration { get; set; }
        public TimeSpan? AudioDiscardedDuration { get; set; }
        public double? PlaybackCompletionRatio { get; set; }
    }
}
