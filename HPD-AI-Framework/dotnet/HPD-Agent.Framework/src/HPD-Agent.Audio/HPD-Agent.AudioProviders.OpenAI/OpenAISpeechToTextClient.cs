// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;

namespace HPD.Agent.Audio.OpenAI;

/// <summary>
/// OpenAI implementation of Microsoft.Extensions.AI ISpeechToTextClient.
/// </summary>
public sealed class OpenAISpeechToTextClient : ISpeechToTextClient
{
    private const string DefaultFilename = "audio.mp3";

    private readonly AudioClient _audioClient;
    private readonly string _defaultModel;
    private bool _disposed;

    /// <summary>
    /// Creates a new OpenAI STT client.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Default model to use.</param>
    /// <param name="baseUrl">Optional OpenAI-compatible endpoint override.</param>
    public OpenAISpeechToTextClient(
        string apiKey,
        string model = "whisper-1",
        string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.Endpoint = new Uri(baseUrl, UriKind.Absolute);
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _audioClient = openAiClient.GetAudioClient(model);
        _defaultModel = model;
    }

    /// <summary>
    /// Creates a new OpenAI STT client from an existing AudioClient.
    /// </summary>
    /// <param name="audioClient">The OpenAI AudioClient to use.</param>
    /// <param name="model">Default model ID for metadata.</param>
    public OpenAISpeechToTextClient(AudioClient audioClient, string model = "whisper-1")
    {
        _audioClient = audioClient ?? throw new ArgumentNullException(nameof(audioClient));
        _defaultModel = model;
    }

    /// <inheritdoc />
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        var filename = GetFilename(audioSpeechStream);

        if (IsTranslationRequest(options))
        {
            var translation = await _audioClient.TranslateAudioAsync(
                audioSpeechStream,
                filename,
                CreateTranslationOptions(options),
                cancellationToken).ConfigureAwait(false);

            return new SpeechToTextResponse(translation.Value.Text)
            {
                ModelId = options?.ModelId ?? _defaultModel,
                RawRepresentation = translation.Value
            };
        }

        var transcription = await _audioClient.TranscribeAudioAsync(
            audioSpeechStream,
            filename,
            CreateTranscriptionOptions(options),
            cancellationToken).ConfigureAwait(false);

        var response = new SpeechToTextResponse(transcription.Value.Text)
        {
            ModelId = options?.ModelId ?? _defaultModel,
            RawRepresentation = transcription.Value
        };

        if (transcription.Value.Segments.Count > 0)
        {
            response.StartTime = transcription.Value.Segments[0].StartTime;
            response.EndTime = transcription.Value.Segments[^1].EndTime;
        }
        else if (transcription.Value.Words.Count > 0)
        {
            response.StartTime = transcription.Value.Words[0].StartTime;
            response.EndTime = transcription.Value.Words[^1].EndTime;
        }

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioSpeechChunks,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechChunks);

        using var audioSpeechStream = new MemoryStream();
        await foreach (var chunk in audioSpeechChunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await audioSpeechStream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        }

        audioSpeechStream.Position = 0;

        foreach (var update in (await GetTextAsync(audioSpeechStream, options, cancellationToken).ConfigureAwait(false)).ToSpeechToTextResponseUpdates())
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        foreach (var update in (await GetTextAsync(audioSpeechStream, options, cancellationToken).ConfigureAwait(false)).ToSpeechToTextResponseUpdates())
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null)
        {
            throw new ArgumentNullException(nameof(serviceType));
        }

        return serviceKey is not null ? null :
            serviceType == typeof(AudioClient) ? _audioClient :
            serviceType == typeof(SpeechToTextClientMetadata) ? new SpeechToTextClientMetadata("openai", _audioClient.Endpoint, _defaultModel) :
            serviceType.IsInstanceOfType(this) ? this :
            null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string GetFilename(Stream stream) =>
        stream is FileStream fileStream ? Path.GetFileName(fileStream.Name) : DefaultFilename;

    private static bool IsTranslationRequest(SpeechToTextOptions? options) =>
        options?.TextLanguage is not null &&
        (options.SpeechLanguage is null || options.SpeechLanguage != options.TextLanguage);

    private AudioTranscriptionOptions CreateTranscriptionOptions(SpeechToTextOptions? options)
    {
        var transcriptionOptions = options?.RawRepresentationFactory?.Invoke(this) as AudioTranscriptionOptions ?? new AudioTranscriptionOptions();
        transcriptionOptions.Language ??= options?.SpeechLanguage;

        if (options?.AdditionalProperties?.TryGetValue("responseFormat", out var responseFormat) == true &&
            responseFormat is string responseFormatText)
        {
            transcriptionOptions.ResponseFormat = ParseTranscriptionFormat(responseFormatText);
        }

        if (options?.AdditionalProperties?.TryGetValue("temperature", out var temperature) == true &&
            temperature is not null &&
            TryConvertSingle(temperature, out var temperatureValue))
        {
            transcriptionOptions.Temperature = temperatureValue;
        }

        return transcriptionOptions;
    }

    private AudioTranslationOptions CreateTranslationOptions(SpeechToTextOptions? options) =>
        options?.RawRepresentationFactory?.Invoke(this) as AudioTranslationOptions ?? new AudioTranslationOptions();

    private static AudioTranscriptionFormat ParseTranscriptionFormat(string responseFormat) =>
        responseFormat.ToLowerInvariant() switch
        {
            "text" => AudioTranscriptionFormat.Text,
            "json" => AudioTranscriptionFormat.Simple,
            "verbose_json" or "verbose" => AudioTranscriptionFormat.Verbose,
            "srt" => AudioTranscriptionFormat.Srt,
            "vtt" => AudioTranscriptionFormat.Vtt,
            _ => AudioTranscriptionFormat.Simple
        };

    private static bool TryConvertSingle(object value, out float result)
    {
        switch (value)
        {
            case float floatValue:
                result = floatValue;
                return true;
            case double doubleValue:
                result = (float)doubleValue;
                return true;
            case decimal decimalValue:
                result = (float)decimalValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
