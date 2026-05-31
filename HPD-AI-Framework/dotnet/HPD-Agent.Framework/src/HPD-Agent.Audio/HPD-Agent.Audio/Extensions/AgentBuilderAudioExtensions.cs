// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Realtime;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Vad;
using HPD.Agent.Audio.Eot;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio;

/// <summary>
/// Extension methods for configuring audio pipeline on AgentBuilder.
/// </summary>
public static class AgentBuilderAudioExtensions
{
    /// <summary>
    /// Adds audio pipeline middleware with configuration.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="configure">Action to configure the audio pipeline middleware.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioPipeline(
        this AgentBuilder builder,
        Action<AudioPipelineMiddleware> configure)
    {
        var middleware = new AudioPipelineMiddleware();
        middleware.ProviderRegistry = builder.ProviderRegistry;
        configure(middleware);
        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds audio pipeline middleware with default configuration.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioPipeline(this AgentBuilder builder)
    {
        return builder.WithMiddleware(new AudioPipelineMiddleware
        {
            ProviderRegistry = builder.ProviderRegistry
        });
    }

    /// <summary>
    /// Adds explicit audio attachment transcription middleware.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="configure">Optional action to configure the middleware.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioAttachmentTranscription(
        this AgentBuilder builder,
        Action<AudioAttachmentTranscriptionMiddleware>? configure = null)
    {
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            ProviderRegistry = builder.ProviderRegistry
        };
        configure?.Invoke(middleware);
        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds explicit audio attachment transcription middleware with a speech recognizer.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="speechRecognizer">The speech recognizer to use for attachment transcription.</param>
    /// <param name="configure">Optional action to configure the middleware.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioAttachmentTranscription(
        this AgentBuilder builder,
        ISpeechRecognizer speechRecognizer,
        Action<AudioAttachmentTranscriptionMiddleware>? configure = null)
    {
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = speechRecognizer,
            ProviderRegistry = builder.ProviderRegistry
        };
        configure?.Invoke(middleware);
        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds audio pipeline middleware with TTS client configuration.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="ttsClient">The text-to-speech client to use.</param>
    /// <param name="configure">Optional additional configuration.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioPipeline(
        this AgentBuilder builder,
        ITextToSpeechClient ttsClient,
        Action<AudioPipelineMiddleware>? configure = null)
    {
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = ttsClient,
            ProviderRegistry = builder.ProviderRegistry
        };
        configure?.Invoke(middleware);
        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds audio pipeline middleware with full provider configuration.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="ttsClient">The text-to-speech client to use.</param>
    /// <param name="speechRecognizer">The HPD speech recognizer to use.</param>
    /// <param name="vad">The voice activity detector to use.</param>
    /// <param name="eotDetector">The end-of-turn detector to use.</param>
    /// <param name="configure">Optional additional configuration.</param>
    /// <returns>The agent builder for chaining.</returns>
    public static AgentBuilder UseAudioPipeline(
        this AgentBuilder builder,
        ITextToSpeechClient? ttsClient,
        ISpeechRecognizer? speechRecognizer,
        IVoiceActivityDetector? vad = null,
        IEotDetector? eotDetector = null,
        Action<AudioPipelineMiddleware>? configure = null)
    {
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = ttsClient,
            SpeechRecognizer = speechRecognizer,
            Vad = vad,
            EotDetector = eotDetector ?? new HeuristicEotDetector(),
            ProviderRegistry = builder.ProviderRegistry
        };
        configure?.Invoke(middleware);
        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Quick setup: Configure audio with a builder action using the new role-based AudioConfig.
    /// </summary>
    public static AgentBuilder WithAudio(this AgentBuilder builder, Action<AudioConfig> configure)
    {
        var config = new AudioConfig();
        configure(config);

        if (config.ProcessingMode == AudioProcessingMode.Realtime)
        {
            return builder.UseRealtimeAudio(config);
        }

        return builder.WithMiddleware(new AudioPipelineMiddleware(config)
        {
            ProviderRegistry = builder.ProviderRegistry
        });
    }

    /// <summary>
    /// Adds true bidirectional realtime audio middleware.
    /// </summary>
    public static AgentBuilder UseRealtimeAudio(
        this AgentBuilder builder,
        AudioConfig? config = null,
        IRealtimeClient? realtimeClient = null)
    {
        var realtimeConfig = config?.Clone() ?? new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig()
        };

        realtimeConfig.ProcessingMode = AudioProcessingMode.Realtime;
        realtimeConfig.Realtime ??= new RealtimeAudioConfig();

        return builder.WithMiddleware(new RealtimeAudioMiddleware(realtimeConfig)
        {
            ProviderRegistry = builder.ProviderRegistry,
            RealtimeClient = realtimeClient
        });
    }

    /// <summary>
    /// Quick setup: Configure audio with a pre-built AudioConfig instance.
    /// </summary>
    public static AgentBuilder WithAudio(this AgentBuilder builder, AudioConfig config)
    {
        return builder.WithAudio(c =>
        {
            var copy = config.Clone();
            c.Tts = copy.Tts;
            c.Stt = copy.Stt;
            c.Vad = copy.Vad;
            c.Eot = copy.Eot;
            c.Realtime = copy.Realtime;
            c.ProcessingMode = copy.ProcessingMode;
            c.IOMode = copy.IOMode;
            c.Language = copy.Language;
            c.Disabled = copy.Disabled;
            c.Diagnostics = copy.Diagnostics;
            c.EnableQuickAnswer = copy.EnableQuickAnswer;
            c.EnableSpeedAdaptation = copy.EnableSpeedAdaptation;
            c.EnablePreemptiveGeneration = copy.EnablePreemptiveGeneration;
            c.PreemptiveGenerationThreshold = copy.PreemptiveGenerationThreshold;
            c.BackchannelStrategy = copy.BackchannelStrategy;
            c.MinWordsForInterruption = copy.MinWordsForInterruption;
            c.EnableFalseInterruptionRecovery = copy.EnableFalseInterruptionRecovery;
            c.FalseInterruptionTimeout = copy.FalseInterruptionTimeout;
            c.ResumeFalseInterruption = copy.ResumeFalseInterruption;
            c.MaxBufferedChunksDuringPause = copy.MaxBufferedChunksDuringPause;
            c.EnableFillerAudio = copy.EnableFillerAudio;
            c.FillerSilenceThreshold = copy.FillerSilenceThreshold;
            c.FillerPhrases = copy.FillerPhrases;
            c.FillerSelectionStrategy = copy.FillerSelectionStrategy;
            c.MaxFillerPlaysPerTurn = copy.MaxFillerPlaysPerTurn;
            c.FillerVoice = copy.FillerVoice;
            c.FillerSpeed = copy.FillerSpeed;
            c.EnableTextFiltering = copy.EnableTextFiltering;
            c.FilterCodeBlocks = copy.FilterCodeBlocks;
            c.FilterTables = copy.FilterTables;
            c.FilterUrls = copy.FilterUrls;
            c.FilterMarkdownFormatting = copy.FilterMarkdownFormatting;
            c.FilterEmoji = copy.FilterEmoji;
        });
    }

    /// <summary>
    /// Quick setup: OpenAI TTS + STT with default settings.
    /// VAD is not configured here — add it separately via WithAudio(audio => audio.Vad = ...) if needed.
    /// </summary>
    public static AgentBuilder WithOpenAIAudio(
        this AgentBuilder builder,
        string? apiKey = null,
        string? ttsVoice = "alloy",
        string? ttsModel = "tts-1",
        string? sttModel = "whisper-1")
    {
        var resolvedApiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        return builder.WithAudio(audio =>
        {
            audio.Tts = new TtsConfig
            {
                Provider = "openai",
                Voice = ttsVoice,
                ModelId = ttsModel,
                ProviderOptionsJson = JsonSerializer.Serialize(new { apiKey = resolvedApiKey })
            };

            audio.Stt = new SttConfig
            {
                Provider = "openai",
                ModelId = sttModel,
                ProviderOptionsJson = JsonSerializer.Serialize(new { apiKey = resolvedApiKey })
            };
        });
    }

    /// <summary>
    /// Quick setup: ElevenLabs TTS + OpenAI STT.
    /// VAD is not configured here — add it separately via WithAudio(audio => audio.Vad = ...) if needed.
    /// </summary>
    public static AgentBuilder WithElevenLabsTts(
        this AgentBuilder builder,
        string elevenLabsApiKey,
        string? voice = "21m00Tcm4TlvDq8ikWAM",
        string? openAiApiKey = null)
    {
        var resolvedOpenAiKey = openAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        return builder.WithAudio(audio =>
        {
            audio.Tts = new TtsConfig
            {
                Provider = "elevenlabs",
                Voice = voice,
                ModelId = "eleven_turbo_v2_5",
                ProviderOptionsJson = JsonSerializer.Serialize(new
                {
                    apiKey = elevenLabsApiKey,
                    stability = 0.5f,
                    similarityBoost = 0.75f
                })
            };

            audio.Stt = new SttConfig
            {
                Provider = "openai",
                ModelId = "whisper-1",
                ProviderOptionsJson = JsonSerializer.Serialize(new { apiKey = resolvedOpenAiKey })
            };
        });
    }
}
