// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;
using HPD.Agent.Audio.Eot;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using HPD.Events;

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
    }

    /// <summary>
    /// Creates a middleware instance with the supplied audio configuration as middleware defaults.
    /// </summary>
    public AudioPipelineMiddleware(AudioConfig config)
    {
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

    /// <summary>STT client (from Microsoft.Extensions.AI).</summary>
    public ISpeechToTextClient? SpeechToTextClient { get; set; }

    /// <summary>TTS client.</summary>
    public ITextToSpeechClient? TextToSpeechClient { get; set; }

    /// <summary>Voice activity detector (optional, for fast interruption).</summary>
    public IVoiceActivityDetector? Vad { get; set; }

    /// <summary>End-of-turn detector.</summary>
    public IEotDetector? EotDetector { get; set; }

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

    private static ISpeechToTextClient WrapSpeechToTextClient(
        ISpeechToTextClient client,
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

    private void EnsureRuntimeProviders(AudioConfig effectiveConfig, IServiceProvider? services)
    {
        var hasAudioInput = HasAudioInputMode(effectiveConfig.IOMode);
        var hasAudioOutput = HasAudioOutputMode(effectiveConfig.IOMode);

        if (SpeechToTextClient == null &&
            effectiveConfig.Stt != null &&
            !string.IsNullOrWhiteSpace(effectiveConfig.Stt.Provider) &&
            hasAudioInput)
        {
            try
            {
                var sttFactory = Stt.SttProviderDiscovery.GetFactory(effectiveConfig.Stt.Provider);
                SpeechToTextClient = WrapSpeechToTextClient(
                    sttFactory.CreateClient(effectiveConfig.Stt, services),
                    effectiveConfig,
                    services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create STT client: {ex.Message}");
            }
        }

        if (TextToSpeechClient == null &&
            effectiveConfig.Tts != null &&
            !string.IsNullOrWhiteSpace(effectiveConfig.Tts.Provider) &&
            hasAudioOutput)
        {
            try
            {
                var ttsFactory = Tts.TtsProviderDiscovery.GetFactory(effectiveConfig.Tts.Provider);
                TextToSpeechClient = WrapTextToSpeechClient(
                    ttsFactory.CreateClient(effectiveConfig.Tts, services),
                    effectiveConfig,
                    services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create TTS client: {ex.Message}");
            }
        }

        if (Vad == null && effectiveConfig.Vad != null && hasAudioInput)
        {
            try
            {
                var vadFactory = Audio.Vad.VadProviderDiscovery.GetFactory(effectiveConfig.Vad.Provider);
                Vad = vadFactory.CreateDetector(effectiveConfig.Vad, services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create VAD: {ex.Message}");
            }
        }

        if (EotDetector == null && effectiveConfig.Eot != null && hasAudioInput)
        {
            try
            {
                var eotFactory = EotProviderDiscovery.GetFactory(effectiveConfig.Eot.Provider);
                EotDetector = eotFactory.CreateDetector(effectiveConfig.Eot, services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create EOT detector: {ex.Message}");
            }
        }
    }

    private async Task RunAudioInputLoopAsync(
        RuntimeHookContext context,
        ChannelReader<AudioInputFrame> frames,
        AudioConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in frames.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
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
            !context.Streams.ActiveStreams.Any(static stream => !stream.IsInterrupted))
        {
            return;
        }

        context.Emit(new UserInterruptedEvent(null)
        {
            Channel = EventChannel.Control
        });

        await context.RunAsync(new InterruptionRequestEvent(
            StreamId: null,
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
        if (SpeechToTextClient == null || buffer.Length == 0)
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
            transcript = await TranscribeAudioAsync(
                new DataContent(buffer.ToArray(), buffer.MimeType),
                effectiveConfig.Stt,
                cancellationToken).ConfigureAwait(false);
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
        context.Emit(new TranscriptionDeltaEvent(transcriptionId, transcript, true, null)
        {
            Channel = EventChannel.Streaming
        });
        context.Emit(new TranscriptionCompletedEvent(transcriptionId, transcript, sttDuration)
        {
            Channel = EventChannel.Synchronous
        });

        var eotProbability = EotDetector?.GetEndOfTurnProbability(transcript) ?? 1.0f;
        var eotConfig = effectiveConfig.Eot ?? new EotConfig();
        var shouldCommit = detectionMethod == AudioInputBuffer.FinalFrameCommitReason ||
            eotConfig.DetectorStrategy == EotDetectionStrategy.Disabled ||
            EotDetector == null ||
            eotProbability >= eotConfig.SilenceBoostMultiplier ||
            CalculateEndpointingDelay(transcript, (float)silenceDuration.TotalSeconds, eotConfig) <= eotConfig.MinEndpointingDelay;

        if (!shouldCommit)
            return;

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
        var effectiveConfig = GetEffectiveConfig((object?)null);
        effectiveConfig.Validate();

        ApplyGlobalLanguage(effectiveConfig);

        if (effectiveConfig.Disabled == true ||
            effectiveConfig.ProcessingMode == AudioProcessingMode.Native ||
            !HasAudioInputMode(effectiveConfig.IOMode))
        {
            return Task.CompletedTask;
        }

        EnsureRuntimeProviders(effectiveConfig, context.Services);

        var subscription = context.EventCoordinator.SubscribeStruct<AudioInputFrame>();
        context.RegisterAsyncDisposable(subscription);
        context.RegisterBackgroundTask(runtimeToken =>
            RunAudioInputLoopAsync(context, subscription.Reader, effectiveConfig, runtimeToken));

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
    /// Processes audio input before LLM call (STT conversion).
    /// Converts audio DataContent in messages to text transcriptions.
    /// Uses role-based discovery to create audio clients dynamically.
    /// </summary>
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var effectiveConfig = GetEffectiveConfig(context.RunConfig?.Audio);

        // Validate merged configuration
        effectiveConfig.Validate();

        ApplyGlobalLanguage(effectiveConfig);

        // Check if audio processing is disabled
        if (effectiveConfig.Disabled == true)
            return;

        // Native mode: the model handles audio I/O directly — skip STT entirely.
        // Audio content is left in the messages for the model to process natively.
        if (effectiveConfig.ProcessingMode == AudioProcessingMode.Native)
            return;

        var hasAudioInput = HasAudioInputMode(effectiveConfig.IOMode);
        var hasAudioOutput = HasAudioOutputMode(effectiveConfig.IOMode);
        EnsureRuntimeProviders(effectiveConfig, context.Services);

        // Check if audio input processing is needed
        if (!hasAudioInput || SpeechToTextClient == null)
            return;

        if (context.Messages == null || context.Messages.Count == 0)
            return;

        var turnStartTime = DateTime.UtcNow;
        _turnMetrics = new TurnMetrics { TurnStartTime = turnStartTime };

        // Process the last user message for audio content
        var lastMessage = context.Messages[^1];
        if (lastMessage.Role != ChatRole.User)
            return;

        // Resolve content store for asset:// URI resolution
        var contentStore = context.Session?.Store?.GetContentStore(context.Session.Id);

        // Collect audio items: AudioContent (typed), DataContent with audio MIME, or UriContent with audio MIME
        var audioItems = new List<(AIContent Original, DataContent? Resolved)>();
        foreach (var content in lastMessage.Contents ?? [])
        {
            if (content is AudioContent ac)
            {
                // Typed AudioContent — may still have bytes or be a data URI
                audioItems.Add((content, ac));
            }
            else if (content is DataContent dc &&
                     dc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
            {
                audioItems.Add((content, dc));
            }
            else if (content is UriContent uc &&
                     uc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true &&
                     uc.Uri?.Scheme == "asset" &&
                     contentStore != null)
            {
                // asset:// URI — resolve bytes from content store
                var assetId = uc.Uri.Host;
                var stored = await contentStore.GetAsync(context.Session!.Id, assetId, cancellationToken);
                if (stored != null)
                {
                    var resolved = new DataContent(stored.Data, stored.ContentType);
                    audioItems.Add((content, resolved));
                }
            }
        }

        if (audioItems.Count == 0)
            return;

        var transcriptionId = Guid.NewGuid().ToString("N")[..8];
        var sttStartTime = DateTime.UtcNow;

        context.TryEmit(new TranscriptionDeltaEvent(transcriptionId, "", false, null)
        {
            Channel = EventChannel.Streaming
        });

        // Transcribe each audio item
        var transcriptions = new List<string>();
        foreach (var (_, resolved) in audioItems)
        {
            if (resolved == null) continue;
            try
            {
                var transcription = await TranscribeAudioAsync(resolved, effectiveConfig.Stt, cancellationToken);
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    transcriptions.Add(transcription);

                    context.TryEmit(new TranscriptionDeltaEvent(transcriptionId, transcription, false, null)
                    {
                        Channel = EventChannel.Streaming
                    });
                }
            }
            catch (Exception ex)
            {
                // Log but continue - don't fail the entire request for STT errors
                context.TryEmit(new AudioPipelineMetricsEvent("error", "stt_error", 1, "count")
                {
                    Channel = EventChannel.Streaming
                });

                System.Diagnostics.Debug.WriteLine($"STT error: {ex.Message}");
            }
        }

        var sttDuration = DateTime.UtcNow - sttStartTime;
        _turnMetrics.SttDuration = sttDuration;

        if (transcriptions.Count > 0)
        {
            var fullTranscription = string.Join(" ", transcriptions);

            // Emit completion event
            context.TryEmit(new TranscriptionCompletedEvent(transcriptionId, fullTranscription, sttDuration)
            {
                Channel = EventChannel.Synchronous
            });

            // Update speed estimate based on transcription
            var wordCount = fullTranscription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (sttDuration.TotalMinutes > 0 && wordCount > 0)
            {
                var wpm = (float)(wordCount / sttDuration.TotalMinutes);
                UpdateSpeedEstimate(wpm, wordCount);
                _turnMetrics.UserWpm = CurrentWpm;
            }

            // Replace audio content with transcription text in the message
            var audioOriginals = audioItems.Select(a => a.Original).ToHashSet(ReferenceEqualityComparer.Instance);
            var newContents = lastMessage.Contents!
                .Where(c => !audioOriginals.Contains(c))
                .ToList();

            // Add transcription as text content at the front
            newContents.Insert(0, new TextContent(fullTranscription));

            // Create new message with updated contents
            var newMessage = new ChatMessage(lastMessage.Role, newContents)
            {
                AuthorName = lastMessage.AuthorName,
                RawRepresentation = lastMessage.RawRepresentation
            };

            // Replace the message in the list
            context.Messages[^1] = newMessage;
        }
    }

    private async Task<string?> TranscribeAudioAsync(
        DataContent audioContent,
        SttConfig? sttConfig,
        CancellationToken cancellationToken)
    {
        if (SpeechToTextClient == null)
            return null;

        var audioData = audioContent.Data;
        if (audioData.IsEmpty)
            return null;

        using var stream = new MemoryStream(audioData.ToArray());

        var result = await SpeechToTextClient.GetTextAsync(
            stream,
            sttConfig?.ToOptions(),
            cancellationToken);

        return result.Text;
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

        if (!string.IsNullOrWhiteSpace(effectiveConfig.Language) && effectiveConfig.Tts != null)
            effectiveConfig.Tts.Language ??= effectiveConfig.Language;

        // Check if audio is disabled for this request
        if (effectiveConfig.Disabled == true)
            return null;

        // Native mode: model outputs audio directly — scan the response stream for
        // DataContent with audio/* MIME and emit AudioChunkEvents; skip TTS entirely.
        if (effectiveConfig.ProcessingMode == AudioProcessingMode.Native &&
            HasAudioOutputMode(effectiveConfig.IOMode))
        {
            return StreamNativeAudioAsync(request, handler, effectiveConfig, ct);
        }

        // Pipeline mode: synthesize audio from text via TTS
        if (TextToSpeechClient == null || !HasAudioOutputMode(effectiveConfig.IOMode))
        {
            // Return null to pass through without interception
            return null;
        }

        // Delegate to implementation method
        return StreamWithTtsAsync(request, handler, effectiveConfig, ct);
    }

    /// <summary>
    /// Implementation of TTS streaming with Quick Answer support.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithTtsAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        AudioConfig effectiveConfig,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Create stream handle for interruption support (from Priority Streaming)
        var stream = request.Streams?.Create();
        var sentenceBuffer = new StringBuilder();
        var synthesisId = Guid.NewGuid().ToString("N")[..8];
        var synthesisState = new SynthesisState();

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
        _fillerTask = MonitorForFillerAsync(request.EventCoordinator, stream, synthesisId, _fillerCts.Token);

        try
        {
            request.EventCoordinator?.Emit(new SynthesisStartedEvent(synthesisId, model, voice)
            {
                Channel = EventChannel.Synchronous,
                StreamId = stream?.StreamId
            });

            await foreach (var update in handler(request).WithCancellation(ct))
            {
                // Cancel filler as soon as first token arrives
                _fillerCts?.Cancel();
                // Extract text from update
                var text = ExtractText(update);
                if (text != null && (effectiveConfig.EnableQuickAnswer ?? true))
                {
                    sentenceBuffer.Append(text);

                    // Quick Answer: synthesize on sentence boundary
                    if (IsSentenceBoundary(sentenceBuffer.ToString()))
                    {
                        var textToSynthesize = FilterTextForTts(sentenceBuffer.ToString(), effectiveConfig);
                        if (!string.IsNullOrWhiteSpace(textToSynthesize))
                        {
                            await foreach (var chunk in SynthesizeAndEmitAsync(
                                request.EventCoordinator, textToSynthesize, stream, synthesisId, ttsOptions, synthesisState, ct))
                            {
                                // Track time to first audio
                                firstAudioTime ??= DateTime.UtcNow;
                            }
                        }
                        sentenceBuffer.Clear();
                    }
                }

                yield return update;
            }

            // Flush remaining text
            if (sentenceBuffer.Length > 0)
            {
                var textToSynthesize = FilterTextForTts(sentenceBuffer.ToString(), effectiveConfig);
                if (!string.IsNullOrWhiteSpace(textToSynthesize))
                {
                    await foreach (var chunk in SynthesizeAndEmitAsync(
                        request.EventCoordinator, textToSynthesize, stream, synthesisId, ttsOptions, synthesisState, ct))
                    {
                        // Track time to first audio
                        firstAudioTime ??= DateTime.UtcNow;
                    }
                }
            }
        }
        finally
        {
            // Ensure filler task is cancelled and awaited
            _fillerCts?.Cancel();
            if (_fillerTask != null)
                await _fillerTask;

            var wasInterrupted = stream?.IsInterrupted ?? false;

            request.EventCoordinator?.Emit(new SynthesisCompletedEvent(synthesisId, wasInterrupted, synthesisState.ChunkIndex, synthesisState.ChunkIndex)
            {
                Channel = EventChannel.Control,
                StreamId = stream?.StreamId,
                CanInterrupt = false
            });
            stream?.Complete();

            // Upload assembled TTS audio to /artifacts if content store is available and synthesis produced audio
            if (synthesisState.AssembledAudio.Count > 0)
            {
                var sessionId = request.Session?.Id;
                var contentStore = request.Session?.Store?.GetContentStore(sessionId ?? "");
                if (contentStore != null && !string.IsNullOrEmpty(sessionId))
                {
                    try
                    {
                        var audioBytes = synthesisState.AssembledAudio.ToArray();
                        await contentStore.PutAsync(
                            scope: sessionId,
                            data: audioBytes,
                            contentType: outputFormat,
                            metadata: new ContentMetadata
                            {
                                Origin = ContentSource.Agent,
                                Tags = new Dictionary<string, string>
                                {
                                    ["folder"]       = "/artifacts",
                                    ["session"]      = sessionId,
                                    ["audio-role"]   = "tts",
                                    ["synthesis-id"] = synthesisId,
                                    ["voice"]        = voice ?? "",
                                    ["model"]        = model ?? "",
                                    ["interrupted"]  = wasInterrupted ? "true" : "false"
                                }
                            });
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
    /// Native mode: passes the LLM stream through unchanged, but extracts any
    /// DataContent items with an audio/* MIME type and emits them as AudioChunkEvents.
    /// No TTS is involved — the model produces audio directly.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamNativeAudioAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        AudioConfig effectiveConfig,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = request.Streams?.Create();
        var synthesisId = Guid.NewGuid().ToString("N")[..8];
        var synthesisState = new SynthesisState();
        var outputFormat = effectiveConfig.Tts?.OutputFormat ?? "audio/pcm";
        var audioFrameEmitter = CreateAudioChunkFrameEmitter(request.EventCoordinator);

        _turnMetrics ??= new TurnMetrics { TurnStartTime = DateTime.UtcNow };
        DateTime? firstAudioTime = null;

        try
        {
            request.EventCoordinator?.Emit(new SynthesisStartedEvent(synthesisId, null, null)
            {
                Channel = EventChannel.Synchronous,
                StreamId = stream?.StreamId
            });

            await foreach (var update in handler(request).WithCancellation(ct))
            {
                // Scan each content item for native audio chunks
                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is DataContent dc &&
                            dc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (stream?.IsInterrupted == true) break;

                            var audioBytes = dc.Data.ToArray();
                            synthesisState.AssembledAudio.AddRange(audioBytes);

                            var audioChunk = new AudioChunkEvent(
                                synthesisId,
                                Convert.ToBase64String(audioBytes),
                                dc.MediaType,
                                synthesisState.ChunkIndex++,
                                TimeSpan.Zero,
                                false)
                            {
                                Channel = EventChannel.Streaming,
                                StreamId = stream?.StreamId,
                                CanInterrupt = true
                            };

                            firstAudioTime ??= DateTime.UtcNow;
                            EmitAudioChunkFrame(audioFrameEmitter, audioChunk, audioBytes);
                            request.EventCoordinator?.Emit(audioChunk);
                        }
                    }
                }

                yield return update;
            }
        }
        finally
        {
            var wasInterrupted = stream?.IsInterrupted ?? false;

            request.EventCoordinator?.Emit(new SynthesisCompletedEvent(synthesisId, wasInterrupted, synthesisState.ChunkIndex, synthesisState.ChunkIndex)
            {
                Channel = EventChannel.Control,
                StreamId = stream?.StreamId,
                CanInterrupt = false
            });
            stream?.Complete();

            // Upload assembled native audio to /artifacts (same contract as Pipeline mode)
            if (synthesisState.AssembledAudio.Count > 0)
            {
                var sessionId = request.Session?.Id;
                var contentStore = request.Session?.Store?.GetContentStore(sessionId ?? "");
                if (contentStore != null && !string.IsNullOrEmpty(sessionId))
                {
                    try
                    {
                        await contentStore.PutAsync(
                            scope: sessionId,
                            data: synthesisState.AssembledAudio.ToArray(),
                            contentType: outputFormat,
                            metadata: new ContentMetadata
                            {
                                Origin = ContentSource.Agent,
                                Tags = new Dictionary<string, string>
                                {
                                    ["folder"]       = "/artifacts",
                                    ["session"]      = sessionId,
                                    ["audio-role"]   = "native",
                                    ["synthesis-id"] = synthesisId,
                                    ["interrupted"]  = wasInterrupted ? "true" : "false"
                                }
                            });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Native audio artifact upload failed: {ex.Message}");
                    }
                }
            }

            var ttsEndTime = DateTime.UtcNow;
            _turnMetrics.TtsDuration = ttsEndTime - _turnMetrics.TurnStartTime;
            _turnMetrics.WasInterrupted = wasInterrupted;
            _turnMetrics.TotalChunks = synthesisState.ChunkIndex;
            _turnMetrics.DeliveredChunks = synthesisState.ChunkIndex;

            if (firstAudioTime.HasValue)
                _turnMetrics.TimeToFirstAudio = firstAudioTime.Value - _turnMetrics.TurnStartTime;

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

        // Reset metrics for next turn
        _turnMetrics = null;
    }

    //
    // VAD INTERRUPT HANDLING (uses core IStreamRegistry)
    //

    internal void OnVadStartOfSpeech(HookContext context, string? transcribedText)
    {
        // Check backchannel strategy before interrupting
        if (BackchannelStrategy == BackchannelStrategy.IgnoreShortUtterances)
        {
            var wordCount = transcribedText?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
            if (wordCount < MinWordsForInterruption)
                return; // Don't interrupt for short utterances
        }

        if (BackchannelStrategy == BackchannelStrategy.IgnoreKnownBackchannels)
        {
            if (IsKnownBackchannel(transcribedText))
                return; // Don't interrupt for "uh-huh", etc.
        }

        // FALSE INTERRUPTION RECOVERY: Pause first, confirm interruption after timeout/transcript
        if (EnableFalseInterruptionRecovery && string.IsNullOrWhiteSpace(transcribedText))
        {
            lock (_pauseLock)
            {
                // If not already paused and have an active stream
                if (_pausedSynthesis == null && context.Streams != null)
                {
                    var activeStreams = context.Streams.GetType()
                        .GetMethod("GetActiveStreams")
                        ?.Invoke(context.Streams, null) as System.Collections.IEnumerable;

                    IStreamHandle? activeStream = null;
                    if (activeStreams != null)
                    {
                        foreach (var stream in activeStreams)
                        {
                            if (stream is IStreamHandle handle && !handle.IsInterrupted)
                            {
                                activeStream = handle;
                                break;
                            }
                        }
                    }

                    if (activeStream != null)
                    {
                        // Pause synthesis
                        _pausedSynthesis = new PausedSynthesisState
                        {
                            SynthesisId = activeStream.StreamId,
                            StreamHandle = activeStream,
                            PausedAt = DateTime.UtcNow
                        };

                        context.Emit(new SpeechPausedEvent(
                            activeStream.StreamId,
                            "potential_interruption")
                        {
                            Channel = EventChannel.Control,
                            StreamId = activeStream.StreamId
                        });

                        // Start timeout timer
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(
                                    TimeSpan.FromSeconds(FalseInterruptionTimeout),
                                    _pausedSynthesis.ResumeTimeoutCts.Token);

                                // Timeout expired - resume if still paused
                                await ResumeIfStillPausedAsync(context);
                            }
                            catch (OperationCanceledException)
                            {
                                // Cancelled - either resumed or interrupted
                            }
                        });

                        return; // Don't interrupt yet, wait for confirmation
                    }
                }
            }
        }

        // Confirmed interruption (or false interruption recovery disabled)
        ConfirmInterruption(context, transcribedText);
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
            StreamId = stateToResume.SynthesisId
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

        await Task.CompletedTask;
    }

    private void ConfirmInterruption(HookContext context, string? transcribedText)
    {
        lock (_pauseLock)
        {
            // Cancel resume timer if running
            _pausedSynthesis?.ResumeTimeoutCts.Cancel();
            _pausedSynthesis = null;
        }

        // Interrupt all active audio streams
        context.Streams?.InterruptAll();

        context.Emit(new UserInterruptedEvent(transcribedText)
        {
            Channel = EventChannel.Control
        });
    }

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
        IStreamHandle? stream,
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
                    StreamId = stream?.StreamId,
                    CanInterrupt = true
                };

                eventCoordinator?.Emit(fillerChunk);
                EmitAudioChunkFrame(
                    eventCoordinator,
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
        string text,
        IStreamHandle? stream,
        string synthesisId,
        TextToSpeechOptions options,
        SynthesisState state,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (stream?.IsInterrupted == true) yield break;
        var audioFrameEmitter = CreateAudioChunkFrameEmitter(eventCoordinator);

        await foreach (var chunk in TextToSpeechClient!.GetStreamingAudioAsync(text, options, ct))
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
                StreamId = stream?.StreamId,
                CanInterrupt = true
            };

            eventCoordinator?.Emit(audioChunk);
            EmitAudioChunkFrame(audioFrameEmitter, audioChunk, audioBytes);
            yield return audioChunk;
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

    private static StructEmitter<AudioChunkFrame>? CreateAudioChunkFrameEmitter(IEventCoordinator? eventCoordinator) =>
        eventCoordinator?.CreateStructEmitter<AudioChunkFrame>(
            new StructEmitterOptions<AudioChunkFrame> { AssignSequenceNumbers = true });

    private static void EmitAudioChunkFrame(
        StructEmitter<AudioChunkFrame>? emitter,
        AudioChunkEvent audioChunk,
        ReadOnlyMemory<byte> audioBytes)
    {
        if (emitter is not { } audioFrames)
            return;

        audioFrames.TryEmit(CreateAudioChunkFrame(audioChunk, audioBytes));
    }

    private static void EmitAudioChunkFrame(
        IEventCoordinator? eventCoordinator,
        AudioChunkEvent audioChunk,
        ReadOnlyMemory<byte> audioBytes)
    {
        var emitter = CreateAudioChunkFrameEmitter(eventCoordinator);
        EmitAudioChunkFrame(emitter, audioChunk, audioBytes);
    }

    private static AudioChunkFrame CreateAudioChunkFrame(
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


    private static bool IsSentenceBoundary(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?');
    }

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
        public required IStreamHandle StreamHandle { get; init; }
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
    }
}
