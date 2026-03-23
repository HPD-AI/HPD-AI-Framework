// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
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
    private readonly AudioConfig _config = new();

    //
    // PROCESSING MODE (HOW audio is processed)
    //

    /// <summary>How audio is processed internally. Default: Pipeline.</summary>
    public AudioProcessingMode ProcessingMode { get; set; } = AudioProcessingMode.Pipeline;

    //
    // I/O MODE (WHAT goes in/out)
    //

    /// <summary>What input/output modalities to use. Default: AudioToAudioAndText.</summary>
    public AudioIOMode IOMode { get; set; } = AudioIOMode.AudioToAudioAndText;

    // Derived helpers for checking I/O capabilities
    /// <summary>Whether audio input is expected.</summary>
    public bool HasAudioInput => IOMode is AudioIOMode.AudioToText
                                        or AudioIOMode.AudioToAudio
                                        or AudioIOMode.AudioToAudioAndText;

    /// <summary>Whether audio output is enabled.</summary>
    public bool HasAudioOutput => IOMode is AudioIOMode.TextToAudio
                                         or AudioIOMode.AudioToAudio
                                         or AudioIOMode.AudioToAudioAndText
                                         or AudioIOMode.TextToAudioAndText;

    /// <summary>Whether text output is enabled (in addition to or instead of audio).</summary>
    public bool HasTextOutput => IOMode is AudioIOMode.AudioToText
                                        or AudioIOMode.AudioToAudioAndText
                                        or AudioIOMode.TextToAudioAndText;

    //
    // PROVIDERS (injected)
    //

    /// <summary>STT client (from Microsoft.Extensions.AI).</summary>
    public ISpeechToTextClient? SpeechToTextClient { get; set; }

    /// <summary>TTS client.</summary>
    public ITextToSpeechClient? TextToSpeechClient { get; set; }

    /// <summary>Voice activity detector (optional, for fast interruption).</summary>
    public IVoiceActivityDetector? Vad { get; set; }

    /// <summary>Turn detector (optional, for end-of-speech detection).</summary>
    public ITurnDetector? TurnDetector { get; set; }

    //
    // TURN DETECTION CONFIGURATION (delegates to AudioConfig)
    //

    /// <summary>Turn detection strategy for silence. Default: FastPath.</summary>
    public TurnDetectionStrategy SilenceStrategy
    {
        get => _config.SilenceStrategy ?? TurnDetectionStrategy.FastPath;
        set => _config.SilenceStrategy = value;
    }

    /// <summary>Turn detection strategy for ML. Default: OnAmbiguous.</summary>
    public TurnDetectionStrategy MlStrategy
    {
        get => _config.MlStrategy ?? TurnDetectionStrategy.OnAmbiguous;
        set => _config.MlStrategy = value;
    }

    /// <summary>Silence threshold for fast-path rejection. Default: 1.5s.</summary>
    public float SilenceFastPathThreshold
    {
        get => _config.SilenceFastPathThreshold ?? 1.5f;
        set => _config.SilenceFastPathThreshold = value;
    }

    /// <summary>Min endpointing delay when ML is confident. Default: 0.3s.</summary>
    public float MinEndpointingDelay
    {
        get => _config.MinEndpointingDelay ?? 0.3f;
        set => _config.MinEndpointingDelay = value;
    }

    /// <summary>Max endpointing delay when ML is uncertain. Default: 1.5s.</summary>
    public float MaxEndpointingDelay
    {
        get => _config.MaxEndpointingDelay ?? 1.5f;
        set => _config.MaxEndpointingDelay = value;
    }

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

    /// <summary>Start LLM inference before turn is confirmed. Reduces latency but uses more compute. Default: false. ( )</summary>
    public bool EnablePreemptiveGeneration
    {
        get => _config.EnablePreemptiveGeneration ?? false;
        set => _config.EnablePreemptiveGeneration = value;
    }

    /// <summary>Minimum turn completion probability to trigger preemptive generation. Default: 0.7. ( )</summary>
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
    /// Per-request values from AudioConfig take precedence over middleware defaults.
    /// </summary>
    private AudioConfig GetEffectiveConfig(AudioConfig? audioOptions)
    {
        // If no per-request overrides, return middleware defaults
        if (audioOptions == null)
            return _config;

        // Merge per-request overrides with middleware defaults
        return _config.MergeWith(audioOptions);
    }

    //
    // MIDDLEWARE HOOKS
    //

    /// <summary>
    /// Processes audio input before LLM call (STT conversion).
    /// Converts audio DataContent in messages to text transcriptions.
    /// Uses role-based discovery to create audio clients dynamically.
    /// </summary>
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        // Support both AudioRunConfig (new slim API) and AudioConfig (legacy full API)
        AudioConfig? audioOptions = context.RunConfig?.Audio switch
        {
            AudioRunConfig runOpts => runOpts.ToFullConfig(),
            AudioConfig cfg => cfg,
            _ => null
        };

        // Merge run-time overrides with middleware defaults
        var effectiveConfig = GetEffectiveConfig(audioOptions);

        // Validate merged configuration
        effectiveConfig.Validate();

        // Check if audio processing is disabled
        if (effectiveConfig.Disabled == true)
            return;

        // Native mode: the model handles audio I/O directly — skip STT entirely.
        // Audio content is left in the messages for the model to process natively.
        if (ProcessingMode == AudioProcessingMode.Native)
            return;

        // Create clients from role-based configuration if not already injected
        // This implements the V3 role-based discovery pattern
        if (SpeechToTextClient == null && effectiveConfig.Stt != null && HasAudioInput)
        {
            try
            {
                var sttFactory = Stt.SttProviderDiscovery.GetFactory(effectiveConfig.Stt.Provider);
                SpeechToTextClient = sttFactory.CreateClient(effectiveConfig.Stt, context.Services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create STT client: {ex.Message}");
            }
        }

        if (TextToSpeechClient == null && effectiveConfig.Tts != null && HasAudioOutput)
        {
            try
            {
                var ttsFactory = Tts.TtsProviderDiscovery.GetFactory(effectiveConfig.Tts.Provider);
                TextToSpeechClient = ttsFactory.CreateClient(effectiveConfig.Tts, context.Services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create TTS client: {ex.Message}");
            }
        }

        if (Vad == null && effectiveConfig.Vad != null && HasAudioInput)
        {
            try
            {
                var vadFactory = Audio.Vad.VadProviderDiscovery.GetFactory(effectiveConfig.Vad.Provider);
                Vad = vadFactory.CreateDetector(effectiveConfig.Vad, context.Services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create VAD: {ex.Message}");
            }
        }

        // Check if audio input processing is needed
        if (!HasAudioInput || SpeechToTextClient == null)
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
            Priority = EventPriority.Normal
        });

        // Transcribe each audio item
        var transcriptions = new List<string>();
        foreach (var (_, resolved) in audioItems)
        {
            if (resolved == null) continue;
            try
            {
                var transcription = await TranscribeAudioAsync(resolved, cancellationToken);
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    transcriptions.Add(transcription);

                    context.TryEmit(new TranscriptionDeltaEvent(transcriptionId, transcription, false, null)
                    {
                        Priority = EventPriority.Normal
                    });
                }
            }
            catch (Exception ex)
            {
                // Log but continue - don't fail the entire request for STT errors
                context.TryEmit(new AudioPipelineMetricsEvent("error", "stt_error", 1, "count")
                {
                    Priority = EventPriority.Background
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
                Priority = EventPriority.Normal
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

    private async Task<string?> TranscribeAudioAsync(DataContent audioContent, CancellationToken cancellationToken)
    {
        if (SpeechToTextClient == null)
            return null;

        var audioData = audioContent.Data;
        if (audioData.IsEmpty)
            return null;

        using var stream = new MemoryStream(audioData.ToArray());

        var result = await SpeechToTextClient.GetTextAsync(stream, cancellationToken: cancellationToken);

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
        // Resolve audio options from request
        var audioOptions = request.RunConfig?.Audio as AudioConfig;

        // Check if audio is disabled for this request
        if (audioOptions?.Disabled == true)
            return null;

        // Native mode: model outputs audio directly — scan the response stream for
        // DataContent with audio/* MIME and emit AudioChunkEvents; skip TTS entirely.
        if (ProcessingMode == AudioProcessingMode.Native && HasAudioOutput)
            return StreamNativeAudioAsync(request, handler, ct);

        // Pipeline mode: synthesize audio from text via TTS
        if (TextToSpeechClient == null || !HasAudioOutput)
        {
            // Return null to pass through without interception
            return null;
        }

        // Delegate to implementation method
        return StreamWithTtsAsync(request, handler, ct);
    }

    /// <summary>
    /// Implementation of TTS streaming with Quick Answer support.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithTtsAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<ChatResponseUpdate>> handler,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var audioOptions = request.RunConfig?.Audio as AudioConfig;

        // Create stream handle for interruption support (from Priority Streaming)
        var stream = request.Streams?.Create();
        var sentenceBuffer = new StringBuilder();
        var synthesisId = Guid.NewGuid().ToString("N")[..8];
        var synthesisState = new SynthesisState();

        // Resolve TTS settings (per-request overrides > middleware defaults)
        var voice = audioOptions?.Tts?.Voice ?? DefaultVoice;
        var model = audioOptions?.Tts?.ModelId ?? DefaultModel;
        var speed = audioOptions?.Tts?.Speed;
        var outputFormat = DefaultOutputFormat ?? "audio/mpeg";

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
                Priority = EventPriority.Normal,
                StreamId = stream?.StreamId
            });

            await foreach (var update in handler(request).WithCancellation(ct))
            {
                // Cancel filler as soon as first token arrives
                _fillerCts?.Cancel();
                // Extract text from update
                var text = ExtractText(update);
                if (text != null && EnableQuickAnswer)
                {
                    sentenceBuffer.Append(text);

                    // Quick Answer: synthesize on sentence boundary
                    if (IsSentenceBoundary(sentenceBuffer.ToString()))
                    {
                        var textToSynthesize = FilterTextForTts(sentenceBuffer.ToString());
                        if (!string.IsNullOrWhiteSpace(textToSynthesize))
                        {
                            await foreach (var chunk in SynthesizeAndEmitAsync(
                                request.EventCoordinator, textToSynthesize, stream, synthesisId, voice, model, speed, synthesisState, ct))
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
                var textToSynthesize = FilterTextForTts(sentenceBuffer.ToString());
                if (!string.IsNullOrWhiteSpace(textToSynthesize))
                {
                    await foreach (var chunk in SynthesizeAndEmitAsync(
                        request.EventCoordinator, textToSynthesize, stream, synthesisId, voice, model, speed, synthesisState, ct))
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
                Priority = EventPriority.Control,
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
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = request.Streams?.Create();
        var synthesisId = Guid.NewGuid().ToString("N")[..8];
        var synthesisState = new SynthesisState();
        var outputFormat = DefaultOutputFormat ?? "audio/pcm";

        _turnMetrics ??= new TurnMetrics { TurnStartTime = DateTime.UtcNow };
        DateTime? firstAudioTime = null;

        try
        {
            request.EventCoordinator?.Emit(new SynthesisStartedEvent(synthesisId, null, null)
            {
                Priority = EventPriority.Normal,
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
                                Priority = EventPriority.Normal,
                                StreamId = stream?.StreamId,
                                CanInterrupt = true
                            };

                            firstAudioTime ??= DateTime.UtcNow;
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
                Priority = EventPriority.Control,
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
                Priority = EventPriority.Background
            });
        }

        if (_turnMetrics.TtsDuration.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "tts_duration", _turnMetrics.TtsDuration.Value.TotalMilliseconds, "ms")
            {
                Priority = EventPriority.Background
            });
        }

        if (_turnMetrics.TimeToFirstAudio.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "time_to_first_audio", _turnMetrics.TimeToFirstAudio.Value.TotalMilliseconds, "ms")
            {
                Priority = EventPriority.Background
            });
        }

        eventCoordinator?.Emit(new AudioPipelineMetricsEvent("latency", "total_latency", totalLatency.TotalMilliseconds, "ms")
        {
            Priority = EventPriority.Background
        });

        if (_turnMetrics.UserWpm.HasValue)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("quality", "user_wpm", _turnMetrics.UserWpm.Value, "wpm")
            {
                Priority = EventPriority.Background
            });
        }

        if (_turnMetrics.WasInterrupted)
        {
            eventCoordinator?.Emit(new AudioPipelineMetricsEvent("quality", "was_interrupted", 1, "bool")
            {
                Priority = EventPriority.Background
            });
        }

        eventCoordinator?.Emit(new AudioPipelineMetricsEvent("throughput", "total_chunks", _turnMetrics.TotalChunks, "chunks")
        {
            Priority = EventPriority.Background
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
                            Priority = EventPriority.Control,
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
            Priority = EventPriority.Control,
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
            Priority = EventPriority.Background
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
            Priority = EventPriority.Immediate
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
    // TURN DETECTION (hybrid strategy)
    //

    internal float CalculateEndpointingDelay(string text, float silenceDuration)
    {
        // Fast path: long silence = definitely done (no ML needed)
        if (SilenceStrategy == TurnDetectionStrategy.FastPath &&
            silenceDuration >= SilenceFastPathThreshold)
        {
            return 0; // Respond immediately
        }

        // ML path: get completion probability
        float mlProbability = 0.5f;
        if (TurnDetector != null && MlStrategy != TurnDetectionStrategy.Disabled)
        {
            mlProbability = TurnDetector.GetCompletionProbability(text);
        }

        // Combine ML probability with silence duration
        // Longer silence → boost probability confidence
        float silenceBoost = Math.Min(silenceDuration / SilenceFastPathThreshold, 1.0f);
        float combinedProbability = Math.Max(mlProbability, silenceBoost * 0.7f);

        // Interpolate delay based on combined probability
        var baseDelay = combinedProbability > 0.7f
            ? MinEndpointingDelay
            : MaxEndpointingDelay - (combinedProbability * (MaxEndpointingDelay - MinEndpointingDelay));

        return AdjustEndpointingDelay(baseDelay);
    }

    //
    // TEXT FILTERING
    //

    internal string FilterTextForTts(string text)
    {
        if (!EnableTextFiltering)
            return text;

        var filtered = text;

        // Remove code blocks
        if (FilterCodeBlocks)
        {
            filtered = CodeBlockRegex().Replace(filtered, " [code omitted] ");
        }

        // Remove tables
        if (FilterTables)
        {
            filtered = TableRegex().Replace(filtered, " [table omitted] ");
        }

        // Remove URLs (keep domain for context)
        if (FilterUrls)
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
        if (FilterMarkdownFormatting)
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
        if (FilterEmoji)
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
                var response = await TextToSpeechClient.GetSpeechAsync(
                    phrase,
                    new TextToSpeechOptions
                    {
                        Voice = _config.FillerVoice ?? DefaultVoice,
                        ModelId = DefaultModel,
                        Speed = _config.FillerSpeed ?? 0.95f
                    },
                    ct);

                _cachedFillers.Add(new CachedFillerAudio
                {
                    Phrase = phrase,
                    AudioData = response.Audio?.Data.ToArray() ?? [],
                    MimeType = response.Audio?.MediaType ?? "audio/mpeg",
                    Duration = response.Duration ?? TimeSpan.Zero
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
                    Priority = EventPriority.Normal,
                    StreamId = stream?.StreamId,
                    CanInterrupt = true
                };

                eventCoordinator?.Emit(fillerChunk);

                eventCoordinator?.Emit(new FillerAudioPlayedEvent(
                    filler.Phrase,
                    filler.Duration)
                {
                    Priority = EventPriority.Background
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
        string? voice,
        string? model,
        float? speed,
        SynthesisState state,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (stream?.IsInterrupted == true) yield break;

        var options = new TextToSpeechOptions
        {
            Voice = voice,
            ModelId = model,
            Speed = speed,
            OutputFormat = DefaultOutputFormat,
            SampleRate = DefaultSampleRate
        };

        await foreach (var chunk in TextToSpeechClient!.GetStreamingSpeechAsync(
            ToAsyncEnumerable(text), options, ct))
        {
            if (stream?.IsInterrupted == true) break;

            var audioData = chunk.Audio?.Data ?? ReadOnlyMemory<byte>.Empty;
            var audioBytes = audioData.ToArray();

            // Accumulate bytes for artifact upload after synthesis completes
            state.AssembledAudio.AddRange(audioBytes);

            var audioChunk = new AudioChunkEvent(
                synthesisId,
                Convert.ToBase64String(audioBytes),
                "audio/mpeg",
                state.ChunkIndex++,
                chunk.Duration ?? TimeSpan.Zero,
                chunk.IsLast)
            {
                Priority = EventPriority.Normal,
                StreamId = stream?.StreamId,
                CanInterrupt = true
            };

            eventCoordinator?.Emit(audioChunk);
            yield return audioChunk;
        }
    }


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

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(string text)
    {
        yield return text;
        await Task.CompletedTask;
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
