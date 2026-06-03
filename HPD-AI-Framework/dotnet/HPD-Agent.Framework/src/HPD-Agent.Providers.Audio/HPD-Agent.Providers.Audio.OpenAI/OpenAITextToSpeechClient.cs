// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.ClientModel;
using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Output;
using Microsoft.Extensions.AI;
using OpenAI.Audio;

namespace HPD.Agent.Providers.Audio.OpenAI;

#pragma warning disable MEAI001
#pragma warning disable OPENAI001

public sealed class OpenAITextToSpeechClient : ITextToSpeechClient
{
    private readonly AudioClient _audioClient;
    private readonly OpenAITtsConfig _providerConfig;
    private readonly string _defaultModelId;
    private readonly string _defaultVoiceId;
    private readonly string _defaultOutputFormat;
    private bool _disposed;

    public OpenAITextToSpeechClient(
        AudioClient audioClient,
        OpenAITtsConfig providerConfig,
        string defaultModelId,
        string defaultVoiceId,
        string defaultOutputFormat)
    {
        _audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _defaultModelId = defaultModelId;
        _defaultVoiceId = defaultVoiceId;
        _defaultOutputFormat = defaultOutputFormat;
    }

    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var voiceId = FirstNonWhiteSpace(options?.VoiceId, _defaultVoiceId)!;
        var outputFormat = FirstNonWhiteSpace(options?.AudioFormat, _providerConfig.OutputFormat, _defaultOutputFormat)!;
        var speechOptions = CreateSpeechOptions(options, outputFormat);
        BinaryData audioData = await _audioClient.GenerateSpeechAsync(
            text,
            ResolveVoice(voiceId),
            speechOptions,
            cancellationToken).ConfigureAwait(false);

        return new TextToSpeechResponse([new DataContent(audioData.ToArray(), ToContentType(outputFormat))])
        {
            ModelId = FirstNonWhiteSpace(options?.ModelId, _defaultModelId),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["voiceId"] = voiceId,
                ["outputFormat"] = outputFormat
            }
        };
    }

    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetAudioAsync(text, options, cancellationToken)
            .ConfigureAwait(false);
        yield return new TextToSpeechResponseUpdate(response.Contents)
        {
            Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
            ModelId = response.ModelId,
            AdditionalProperties = response.AdditionalProperties
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(AudioClient))
            return _audioClient;

        if (serviceType == typeof(OpenAITtsConfig))
            return _providerConfig;

        if (serviceType == typeof(TextToSpeechClientMetadata))
            return new TextToSpeechClientMetadata(
                OpenAIAudioProvider.Key,
                new Uri("https://platform.openai.com/docs/guides/audio"),
                _defaultModelId);

        if (serviceType == typeof(TextToSpeechCapabilityProfile))
            return new TextToSpeechCapabilityProfile
            {
                SupportsCompletedTextSynthesis = true,
                SupportsCompletedTextAudioStreaming = false,
                SupportsPushTextAudioStreaming = false,
                SupportsAlignment = false,
                SupportsCancellationBeforeAudio = true,
                SupportsCancellationAfterAudio = false,
                PreferredStreamingFormats = []
            };

        return null;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private SpeechGenerationOptions CreateSpeechOptions(
        TextToSpeechOptions? options,
        string outputFormat)
    {
        var speechOptions = new SpeechGenerationOptions
        {
            ResponseFormat = ResolveFormat(outputFormat)
        };

        var speed = options?.Speed ?? _providerConfig.Speed;
        if (speed.HasValue)
        {
            speechOptions.SpeedRatio = speed.Value;
        }

        return speechOptions;
    }

    private static GeneratedSpeechVoice ResolveVoice(string voiceName) =>
        voiceName.ToLowerInvariant() switch
        {
            "alloy" => GeneratedSpeechVoice.Alloy,
            "ash" => GeneratedSpeechVoice.Ash,
            "coral" => GeneratedSpeechVoice.Coral,
            "echo" => GeneratedSpeechVoice.Echo,
            "fable" => GeneratedSpeechVoice.Fable,
            "onyx" => GeneratedSpeechVoice.Onyx,
            "nova" => GeneratedSpeechVoice.Nova,
            "sage" => GeneratedSpeechVoice.Sage,
            "shimmer" => GeneratedSpeechVoice.Shimmer,
            _ => GeneratedSpeechVoice.Nova
        };

    private static GeneratedSpeechFormat ResolveFormat(string outputFormat) =>
        outputFormat.ToLowerInvariant() switch
        {
            "mp3" or "audio/mpeg" => GeneratedSpeechFormat.Mp3,
            "opus" or "audio/opus" => GeneratedSpeechFormat.Opus,
            "aac" or "audio/aac" => GeneratedSpeechFormat.Aac,
            "flac" or "audio/flac" => GeneratedSpeechFormat.Flac,
            "wav" or "audio/wav" => GeneratedSpeechFormat.Wav,
            "pcm" or "audio/pcm" => GeneratedSpeechFormat.Pcm,
            _ => GeneratedSpeechFormat.Mp3
        };

    private static string ToContentType(string outputFormat) =>
        outputFormat.ToLowerInvariant() switch
        {
            "mp3" or "audio/mpeg" => "audio/mpeg",
            "opus" or "audio/opus" => "audio/opus",
            "aac" or "audio/aac" => "audio/aac",
            "flac" or "audio/flac" => "audio/flac",
            "wav" or "audio/wav" => "audio/wav",
            "pcm" or "audio/pcm" => "audio/pcm",
            var value when value.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => value,
            _ => "audio/mpeg"
        };

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
