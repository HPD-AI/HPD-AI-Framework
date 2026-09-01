// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI;
using OpenAI.Audio;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Providers.Audio.OpenAI;

#pragma warning disable OPENAI001

internal sealed class OpenAIConfiguringSpeechToTextClient : ISpeechToTextClient
{
    private readonly ISpeechToTextClient _innerClient;
    private readonly OpenAISttOptions _providerConfig;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _modelId;
    private readonly string? _languageCode;
    private readonly IReadOnlyDictionary<string, string>? _headers;

    public OpenAIConfiguringSpeechToTextClient(
        ISpeechToTextClient innerClient,
        OpenAISttOptions providerConfig,
        string apiKey,
        Uri endpoint,
        string modelId,
        string? languageCode,
        IReadOnlyDictionary<string, string>? headers)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _apiKey = apiKey;
        _endpoint = endpoint;
        _modelId = modelId;
        _languageCode = languageCode;
        _headers = headers;
    }

    public void Dispose() => _innerClient.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is null && serviceType == typeof(IStreamingSpeechToTextParticipantFactory))
            return new OpenAIRealtimeSpeechToTextParticipantFactory(_apiKey, _endpoint, _modelId,
                _languageCode, _providerConfig, _headers);

        return _innerClient.GetService(serviceType, serviceKey);
    }

    public Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
        => _innerClient.GetTextAsync(
            audioSpeechStream,
            ConfigureOptions(options),
            cancellationToken);

    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
        => _innerClient.GetStreamingTextAsync(
            audioSpeechStream,
            ConfigureOptions(options),
            cancellationToken);

    private SpeechToTextOptions ConfigureOptions(SpeechToTextOptions? source)
    {
        var options = source?.Clone() ?? new SpeechToTextOptions();
        var previousFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var raw = previousFactory?.Invoke(client);
            var transcriptionOptions = raw as AudioTranscriptionOptions
                ?? new AudioTranscriptionOptions();

            ApplyOpenAIOptions(transcriptionOptions, options);
            return transcriptionOptions;
        };

        return options;
    }

    private void ApplyOpenAIOptions(
        AudioTranscriptionOptions target,
        SpeechToTextOptions source)
    {
        target.Language ??= source.SpeechLanguage;
        target.Prompt ??= GetString(source, OpenAISttOptionKeys.Prompt)
            ?? _providerConfig.Prompt;
        target.Temperature ??= GetSingle(source, OpenAISttOptionKeys.Temperature)
            ?? _providerConfig.Temperature;

        var responseFormat = GetString(source, OpenAISttOptionKeys.ResponseFormat)
            ?? _providerConfig.ResponseFormat;
        if (!string.IsNullOrWhiteSpace(responseFormat))
        {
            target.ResponseFormat ??= ParseTranscriptionFormat(responseFormat);
        }

        var granularities = GetStringArray(source, OpenAISttOptionKeys.TimestampGranularities)
            ?? _providerConfig.TimestampGranularities;
        if (granularities is { Length: > 0 })
        {
            target.TimestampGranularities = ParseTimestampGranularities(granularities);
        }

        var includeLogprobs = GetBoolean(source, OpenAISttOptionKeys.IncludeLogprobs)
            ?? _providerConfig.IncludeLogprobs;
        if (includeLogprobs is true)
        {
            target.Includes = AudioTranscriptionIncludes.Logprobs;
        }
    }

    private static string? GetString(SpeechToTextOptions source, string key)
        => source.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value as string
            : null;

    private static float? GetSingle(SpeechToTextOptions source, string key)
    {
        if (source.AdditionalProperties?.TryGetValue(key, out var value) != true)
        {
            return null;
        }

        return value switch
        {
            float single => single,
            double d => (float)d,
            decimal d => (float)d,
            int i => i,
            long l => l,
            string s when float.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? GetBoolean(SpeechToTextOptions source, string key)
    {
        if (source.AdditionalProperties?.TryGetValue(key, out var value) != true)
        {
            return null;
        }

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static string[]? GetStringArray(SpeechToTextOptions source, string key)
    {
        if (source.AdditionalProperties?.TryGetValue(key, out var value) != true)
        {
            return null;
        }

        return value switch
        {
            string s => SplitList(s),
            string[] values => values,
            IEnumerable<string> values => values.ToArray(),
            _ => null
        };
    }

    private static string[] SplitList(string value)
        => value
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static AudioTranscriptionFormat ParseTranscriptionFormat(string value)
        => Normalize(value) switch
        {
            "text" => AudioTranscriptionFormat.Text,
            "json" or "simple" or "simplejson" => AudioTranscriptionFormat.Simple,
            "verbose" or "verbosejson" => AudioTranscriptionFormat.Verbose,
            "srt" => AudioTranscriptionFormat.Srt,
            "vtt" => AudioTranscriptionFormat.Vtt,
            _ => throw new ArgumentException(
                $"Unsupported OpenAI transcription response format '{value}'. " +
                "Use text, json, verbose_json, srt, or vtt.")
        };

    private static AudioTimestampGranularities ParseTimestampGranularities(
        IEnumerable<string> values)
    {
        var result = AudioTimestampGranularities.Default;
        foreach (var value in values)
        {
            result |= Normalize(value) switch
            {
                "word" or "words" => AudioTimestampGranularities.Word,
                "segment" or "segments" => AudioTimestampGranularities.Segment,
                "default" => AudioTimestampGranularities.Default,
                _ => throw new ArgumentException(
                    $"Unsupported OpenAI timestamp granularity '{value}'. Use word or segment.")
            };
        }

        return result;
    }

    private static string Normalize(string value)
        => value
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
}

#pragma warning restore OPENAI001
