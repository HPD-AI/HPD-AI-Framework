// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent.Audio.Tts;
using HPD.Agent.AudioProviders.ElevenLabs.Tts;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.ElevenLabs;

/// <summary>
/// ElevenLabs text-to-speech client implementing ITextToSpeechClient.
/// Supports HTTP streaming TTS using the ElevenLabs SDK.
/// Updated to use V3 role-based configuration (TtsConfig + ElevenLabsTtsConfig).
/// </summary>
public sealed class ElevenLabsTextToSpeechClient : ITextToSpeechClient
{
    private readonly TtsConfig _ttsConfig;
    private readonly ElevenLabsTtsConfig _providerConfig;
    private readonly global::ElevenLabs.TextToSpeechClient _client;
    private bool _disposed;

    /// <summary>
    /// Creates a new ElevenLabs TTS client using role-based configuration.
    /// </summary>
    /// <param name="ttsConfig">Service-agnostic TTS configuration (Voice, Speed, ModelId, etc.)</param>
    /// <param name="providerConfig">ElevenLabs-specific configuration (Stability, SimilarityBoost, etc.)</param>
    /// <param name="httpClient">Optional HttpClient for connection pooling</param>
    public ElevenLabsTextToSpeechClient(
        TtsConfig ttsConfig,
        ElevenLabsTtsConfig providerConfig,
        HttpClient? httpClient = null)
    {
        _ttsConfig = ttsConfig ?? throw new ArgumentNullException(nameof(ttsConfig));
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));

        // Validate API key
        var apiKey = _providerConfig.ApiKey ?? Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key is required. Set it via ProviderOptionsJson or ELEVENLABS_API_KEY environment variable.");

        _client = new global::ElevenLabs.TextToSpeechClient(
            httpClient: httpClient,
            authorizations: [
                new global::ElevenLabs.EndPointAuthorization
                {
                    Type = "ApiKey",
                    Location = "Header",
                    Name = "xi-api-key",
                    Value = apiKey
                }
            ]
        );
    }

    /// <summary>
    /// DEPRECATED: Creates a new ElevenLabs TTS client using legacy monolithic configuration.
    /// This constructor is kept for backwards compatibility only.
    /// Use the TtsConfig + ElevenLabsTtsConfig constructor instead.
    /// </summary>
    [Obsolete("Use the constructor with TtsConfig + ElevenLabsTtsConfig instead. This will be removed in a future version.")]
    public ElevenLabsTextToSpeechClient(
        ElevenLabsAudioConfig config,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        // Convert legacy config to role-based config
        _ttsConfig = new TtsConfig
        {
            Voice = config.DefaultVoiceId,
            Speed = config.Speed,
            ModelId = config.ModelId,
            Language = config.LanguageCode,
            OutputFormat = config.OutputFormat
        };

        _providerConfig = new ElevenLabsTtsConfig
        {
            ApiKey = config.ApiKey,
            BaseUrl = config.BaseUrl,
            WebSocketUrl = config.WebSocketUrl,
            Stability = config.Stability,
            SimilarityBoost = config.SimilarityBoost,
            Style = config.Style,
            UseSpeakerBoost = config.UseSpeakerBoost,
            ChunkLengthSchedule = config.ChunkLengthSchedule,
            EnableWordTimestamps = config.EnableWordTimestamps,
            Seed = config.Seed,
            ApplyTextNormalization = config.ApplyTextNormalization,
            PreviousText = config.PreviousText,
            NextText = config.NextText,
            PreviousRequestIds = config.PreviousRequestIds,
            NextRequestIds = config.NextRequestIds,
            PronunciationDictionaryId = config.PronunciationDictionaryId,
            PronunciationDictionaryVersionId = config.PronunciationDictionaryVersionId,
            HttpTimeoutSeconds = (int)config.HttpTimeout.TotalSeconds,
            WebSocketConnectTimeoutSeconds = (int)config.WebSocketConnectTimeout.TotalSeconds,
            WebSocketReceiveTimeoutSeconds = (int)config.WebSocketReceiveTimeout.TotalSeconds
        };

        _client = new global::ElevenLabs.TextToSpeechClient(
            httpClient: httpClient,
            authorizations: [
                new global::ElevenLabs.EndPointAuthorization
                {
                    Type = "ApiKey",
                    Location = "Header",
                    Name = "xi-api-key",
                    Value = config.ApiKey!
                }
            ]
        );
    }

    /// <summary>
    /// Get speech audio from text (non-streaming).
    /// </summary>
    public async Task<TextToSpeechResponse> GetSpeechAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Service-agnostic settings from TtsConfig
        var voiceId = options?.Voice ?? _ttsConfig.Voice ?? "21m00Tcm4TlvDq8ikWAM"; // Rachel default
        var modelId = options?.ModelId ?? _ttsConfig.ModelId ?? "eleven_turbo_v2_5";
        var language = options?.Language ?? _ttsConfig.Language;

        var audioBytes = await _client.CreateTextToSpeechByVoiceIdStreamAsync(
            voiceId: voiceId,
            text: text,
            modelId: modelId,
            languageCode: language,
            cancellationToken: cancellationToken
        );

        return new TextToSpeechResponse
        {
            Audio = new DataContent(audioBytes, "audio/mpeg"),
            ModelId = modelId,
            Voice = voiceId
        };
    }

    /// <summary>
    /// Get speech audio from streaming text chunks.
    /// Streams audio chunks as they are generated.
    /// </summary>
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
        IAsyncEnumerable<string> textChunks,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textChunks);

        // Service-agnostic settings from TtsConfig
        var voiceId = options?.Voice ?? _ttsConfig.Voice ?? "21m00Tcm4TlvDq8ikWAM"; // Rachel default
        var modelId = options?.ModelId ?? _ttsConfig.ModelId ?? "eleven_turbo_v2_5";
        var language = options?.Language ?? _ttsConfig.Language;

        int sequenceNumber = 0;

        await foreach (var textChunk in textChunks.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(textChunk))
            {
                continue;
            }

            var audioBytes = await _client.CreateTextToSpeechByVoiceIdStreamAsync(
                voiceId: voiceId,
                text: textChunk,
                modelId: modelId,
                languageCode: language,
                cancellationToken: cancellationToken
            );

            yield return new TextToSpeechResponseUpdate
            {
                Audio = new DataContent(audioBytes, "audio/mpeg"),
                SequenceNumber = sequenceNumber++
            };
        }
    }

    /// <summary>
    /// Gets a service of the specified type.
    /// </summary>
    public object? GetService(Type serviceType, object? key = null)
    {
        if (serviceType == typeof(TtsConfig))
        {
            return _ttsConfig;
        }

        if (serviceType == typeof(ElevenLabsTtsConfig))
        {
            return _providerConfig;
        }

        if (serviceType == typeof(global::ElevenLabs.TextToSpeechClient))
        {
            return _client;
        }

        return null;
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
    }
}
