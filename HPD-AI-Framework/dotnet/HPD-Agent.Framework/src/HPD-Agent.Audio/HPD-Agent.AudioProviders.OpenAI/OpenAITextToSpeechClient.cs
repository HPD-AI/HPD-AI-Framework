// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;

namespace HPD.Agent.Audio.OpenAI;

/// <summary>
/// OpenAI implementation of ITextToSpeechClient.
/// Uses OpenAI's TTS API for speech synthesis.
/// </summary>
public class OpenAITextToSpeechClient : ITextToSpeechClient
{
    private readonly AudioClient _audioClient;
    private readonly string _defaultModel;
    private readonly string _defaultVoice;
    private bool _disposed;

    /// <summary>
    /// Creates a new OpenAI TTS client.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Default model to use (default: "tts-1").</param>
    /// <param name="voice">Default voice to use (default: "nova").</param>
    public OpenAITextToSpeechClient(
        string apiKey,
        string model = "tts-1",
        string voice = "nova")
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        var openAiClient = new OpenAIClient(apiKey);
        _audioClient = openAiClient.GetAudioClient(model);
        _defaultModel = model;
        _defaultVoice = voice;
    }

    /// <summary>
    /// Creates a new OpenAI TTS client from an existing AudioClient.
    /// </summary>
    /// <param name="audioClient">The OpenAI AudioClient to use.</param>
    /// <param name="model">Default model ID for metadata.</param>
    /// <param name="voice">Default voice to use.</param>
    public OpenAITextToSpeechClient(
        AudioClient audioClient,
        string model = "tts-1",
        string voice = "nova")
    {
        _audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
        _defaultModel = model;
        _defaultVoice = voice;
    }

    /// <inheritdoc />
    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(text);

        var voiceId = options?.VoiceId ?? _defaultVoice;
        var voice = ResolveVoice(voiceId);
        var speechOptions = CreateSpeechOptions(options);

        BinaryData audioData = await _audioClient.GenerateSpeechAsync(
            text,
            voice,
            speechOptions,
            cancellationToken);

        return new TextToSpeechResponse([new DataContent(audioData.ToArray(), GetAudioFormat(options))])
        {
            ModelId = options?.ModelId ?? _defaultModel,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["voiceId"] = voiceId
            }
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(text);

        var voiceId = options?.VoiceId ?? _defaultVoice;
        var voice = ResolveVoice(voiceId);
        var speechOptions = CreateSpeechOptions(options);

        // Generate speech
        BinaryData audioData = await _audioClient.GenerateSpeechAsync(
            text,
            voice,
            speechOptions,
            cancellationToken);

        // Return as single chunk (OpenAI TTS returns complete audio)
        yield return new TextToSpeechResponseUpdate([new DataContent(audioData.ToArray(), GetAudioFormat(options))])
        {
            Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
            ModelId = options?.ModelId ?? _defaultModel,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["voiceId"] = voiceId
            }
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(AudioClient))
            return _audioClient;

        if (serviceType == typeof(TextToSpeechClientMetadata))
            return new TextToSpeechClientMetadata("openai", null, _defaultModel);

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static GeneratedSpeechVoice ResolveVoice(string voiceName)
    {
        return voiceName.ToLowerInvariant() switch
        {
            "alloy" => GeneratedSpeechVoice.Alloy,
            "echo" => GeneratedSpeechVoice.Echo,
            "fable" => GeneratedSpeechVoice.Fable,
            "onyx" => GeneratedSpeechVoice.Onyx,
            "nova" => GeneratedSpeechVoice.Nova,
            "shimmer" => GeneratedSpeechVoice.Shimmer,
            "ash" => GeneratedSpeechVoice.Ash,
            "coral" => GeneratedSpeechVoice.Coral,
            "sage" => GeneratedSpeechVoice.Sage,
            _ => GeneratedSpeechVoice.Nova // Default to Nova
        };
    }

    private static SpeechGenerationOptions CreateSpeechOptions(TextToSpeechOptions? options)
    {
        var speechOptions = new SpeechGenerationOptions();

        if (options?.Speed.HasValue == true)
        {
            speechOptions.SpeedRatio = options.Speed.Value;
        }

        if (!string.IsNullOrEmpty(options?.AudioFormat))
        {
            speechOptions.ResponseFormat = options.AudioFormat.ToLowerInvariant() switch
            {
                "mp3" or "audio/mpeg" => GeneratedSpeechFormat.Mp3,
                "opus" or "audio/opus" => GeneratedSpeechFormat.Opus,
                "aac" or "audio/aac" => GeneratedSpeechFormat.Aac,
                "flac" or "audio/flac" => GeneratedSpeechFormat.Flac,
                "wav" or "audio/wav" => GeneratedSpeechFormat.Wav,
                "pcm" or "audio/pcm" => GeneratedSpeechFormat.Pcm,
                _ => GeneratedSpeechFormat.Mp3
            };
        }

        return speechOptions;
    }

    private static string GetAudioFormat(TextToSpeechOptions? options) =>
        options?.AudioFormat?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "pcm" => "audio/pcm",
            string format when format.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => format,
            _ => "audio/mpeg"
        };
}
