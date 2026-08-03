// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public sealed class ElevenLabsTextToSpeechClient : ITextToSpeechClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly string _defaultModelId;
    private readonly string _defaultVoiceId;
    private readonly string _defaultOutputFormat;
    private readonly ElevenLabsTtsRuntimeSettings _providerConfig;
    private readonly IPushTextToSpeechStreamFactory? _pushTextStreamFactory;
    private bool _disposed;

    internal ElevenLabsTextToSpeechClient(
        string apiKey,
        ElevenLabsTtsRuntimeSettings providerConfig,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(providerConfig);

        _apiKey = apiKey;
        _providerConfig = providerConfig;
        _baseUri = new Uri(FirstNonWhiteSpace(providerConfig.BaseUrl, ElevenLabsAudioProvider.DefaultBaseUrl)!, UriKind.Absolute);
        _defaultModelId = FirstNonWhiteSpace(providerConfig.DefaultModelId, ElevenLabsAudioProvider.DefaultTextToSpeechModel)!;
        _defaultVoiceId = FirstNonWhiteSpace(providerConfig.DefaultVoiceId, ElevenLabsAudioProvider.DefaultVoiceId)!;
        _defaultOutputFormat = FirstNonWhiteSpace(providerConfig.OutputFormat, ElevenLabsAudioProvider.DefaultOutputFormat)!;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _pushTextStreamFactory = providerConfig.EnablePushTextStreaming
            ? new ElevenLabsPushTextToSpeechStreamFactory(
                _apiKey,
                _providerConfig,
                _defaultModelId,
                _defaultVoiceId,
                _defaultOutputFormat)
            : null;
    }

    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var modelId = FirstNonWhiteSpace(options?.ModelId, _defaultModelId)!;
        var voiceId = FirstNonWhiteSpace(options?.VoiceId, _defaultVoiceId)!;
        var outputFormat = NormalizeOutputFormat(FirstNonWhiteSpace(options?.AudioFormat, _defaultOutputFormat)!);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildTextToSpeechUri(voiceId, outputFormat));
        request.Headers.TryAddWithoutValidation("xi-api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ToContentType(outputFormat)));

        var requestBody = new ElevenLabsTtsRequest
        {
            Text = text,
            ModelId = modelId,
            LanguageCode = options?.Language,
            ApplyTextNormalization = _providerConfig.ApplyTextNormalization,
            VoiceSettings = CreateVoiceSettings(options)
        };
        var json = JsonSerializer.Serialize(
            requestBody,
            ElevenLabsTtsJsonContext.Default.ElevenLabsTtsRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new TextToSpeechResponse([new DataContent(audioBytes, ToContentType(outputFormat))])
        {
            ModelId = modelId,
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
        var response = await GetAudioAsync(text, options, cancellationToken);
        yield return new TextToSpeechResponseUpdate(response.Contents)
        {
            Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
            ModelId = response.ModelId,
            AdditionalProperties = response.AdditionalProperties
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(HttpClient))
            return _httpClient;

        if (serviceType == typeof(TextToSpeechClientMetadata))
            return new TextToSpeechClientMetadata(
                ElevenLabsAudioProvider.Key,
                new Uri("https://elevenlabs.io/docs"),
                _defaultModelId);

        if (serviceType == typeof(TextToSpeechCapabilityProfile))
            return new TextToSpeechCapabilityProfile
            {
                SupportsCompletedTextSynthesis = true,
                SupportsCompletedTextAudioStreaming = false,
                SupportsPushTextAudioStreaming = _pushTextStreamFactory is not null,
                SupportsAlignment = _providerConfig.SyncAlignment is true,
                SupportsCancellationBeforeAudio = true,
                SupportsCancellationAfterAudio = _pushTextStreamFactory is not null,
                PreferredStreamingFormats = _pushTextStreamFactory is null
                    ? []
                    : PreferredStreamingFormats(_defaultOutputFormat)
            };

        if (serviceType == typeof(IPushTextToSpeechStreamFactory))
            return _pushTextStreamFactory;

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private Uri BuildTextToSpeechUri(string voiceId, string outputFormat)
    {
        var baseUri = _baseUri.ToString().TrimEnd('/');
        var escapedVoiceId = Uri.EscapeDataString(voiceId);
        var escapedOutputFormat = Uri.EscapeDataString(outputFormat);
        return new Uri($"{baseUri}/text-to-speech/{escapedVoiceId}?output_format={escapedOutputFormat}", UriKind.Absolute);
    }

    private static async ValueTask EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"ElevenLabs API call failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"ElevenLabs API call failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}";

        throw new HttpRequestException(
            message,
            inner: null,
            statusCode: response.StatusCode);
    }

    private ElevenLabsVoiceSettings? CreateVoiceSettings(TextToSpeechOptions? options)
    {
        var speed = options?.Speed.HasValue == true
            ? options.Speed.Value
            : _providerConfig.Speed;

        if (_providerConfig.Stability is null &&
            _providerConfig.SimilarityBoost is null &&
            _providerConfig.Style is null &&
            _providerConfig.UseSpeakerBoost is null &&
            speed is null)
        {
            return null;
        }

        return new ElevenLabsVoiceSettings
        {
            Stability = _providerConfig.Stability,
            SimilarityBoost = _providerConfig.SimilarityBoost,
            Style = _providerConfig.Style,
            UseSpeakerBoost = _providerConfig.UseSpeakerBoost,
            Speed = speed
        };
    }

    internal static string NormalizeOutputFormat(string outputFormat) =>
        outputFormat.ToLowerInvariant() switch
        {
            "mp3" or "audio/mpeg" => ElevenLabsAudioProvider.DefaultOutputFormat,
            "wav" or "audio/wav" => "wav",
            "pcm" or "audio/pcm" => "pcm_16000",
            var value when value.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => ElevenLabsAudioProvider.DefaultOutputFormat,
            var value => value
        };

    private static IReadOnlyList<string> PreferredStreamingFormats(string defaultOutputFormat)
    {
        var normalizedDefault = NormalizeOutputFormat(defaultOutputFormat);
        return string.Equals(normalizedDefault, "pcm_16000", StringComparison.OrdinalIgnoreCase)
            ? ["pcm_16000"]
            : ["pcm_16000", normalizedDefault];
    }

    internal static string ToContentType(string outputFormat) =>
        outputFormat.ToLowerInvariant() switch
        {
            var value when value.StartsWith("mp3", StringComparison.Ordinal) => "audio/mpeg",
            var value when value.StartsWith("wav", StringComparison.Ordinal) => "audio/wav",
            var value when value.StartsWith("pcm", StringComparison.Ordinal) => "audio/pcm",
            var value when value.StartsWith("ulaw", StringComparison.Ordinal) => "audio/basic",
            "opus" or "audio/opus" => "audio/opus",
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
