// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Buffers;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public sealed class ElevenLabsSpeechToTextClient : ISpeechToTextClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly string _defaultModelId;
    private readonly string _defaultRealtimeModelId;
    private readonly ElevenLabsSttConfig _providerConfig;
    private readonly Func<ClientWebSocket> _webSocketFactory;
    private bool _disposed;

    public ElevenLabsSpeechToTextClient(
        string apiKey,
        ElevenLabsSttConfig providerConfig,
        HttpClient? httpClient = null,
        Func<ClientWebSocket>? webSocketFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(providerConfig);

        _apiKey = apiKey;
        _providerConfig = providerConfig;
        _baseUri = new Uri(FirstNonWhiteSpace(providerConfig.BaseUrl, ElevenLabsAudioProvider.DefaultBaseUrl)!, UriKind.Absolute);
        _defaultModelId = FirstNonWhiteSpace(providerConfig.DefaultModelId, ElevenLabsAudioProvider.DefaultSpeechToTextModel)!;
        _defaultRealtimeModelId = FirstNonWhiteSpace(
            providerConfig.RealtimeModelId,
            ElevenLabsAudioProvider.DefaultRealtimeSpeechToTextModel)!;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _webSocketFactory = webSocketFactory ?? (() => new ClientWebSocket());
    }

    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        var modelId = FirstNonWhiteSpace(options?.ModelId, _defaultModelId)!;
        var languageCode = FirstNonWhiteSpace(options?.SpeechLanguage, _providerConfig.LanguageCode);
        var requestModel = CreateRequestModel(modelId, languageCode, options);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildSpeechToTextUri());
        request.Headers.TryAddWithoutValidation("xi-api-key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var content = new MultipartFormDataContent();
        AddStringContent(content, "model_id", requestModel.ModelId);
        AddStringContent(content, "language_code", requestModel.LanguageCode);
        AddBoolContent(content, "diarize", requestModel.Diarize);
        AddBoolContent(content, "tag_audio_events", requestModel.TagAudioEvents);
        AddStringContent(content, "timestamps_granularity", requestModel.TimestampsGranularity);

        var audioBytes = await ReadAllBytesAsync(audioSpeechStream, cancellationToken).ConfigureAwait(false);
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveContentType(options));
        content.Add(fileContent, "file", ResolveFileName(audioSpeechStream, options));
        request.Content = content;

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(
            responseStream,
            ElevenLabsTtsJsonContext.Default.ElevenLabsSpeechToTextResponse,
            cancellationToken).ConfigureAwait(false) ?? new ElevenLabsSpeechToTextResponse();

        return ToSpeechToTextResponse(result, requestModel);
    }

    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        using var webSocket = _webSocketFactory();
        webSocket.Options.SetRequestHeader("xi-api-key", _apiKey);
        var request = CreateRealtimeRequestModel(options);

        await webSocket.ConnectAsync(BuildRealtimeSpeechToTextUri(request), cancellationToken)
            .ConfigureAwait(false);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sendTask = SendStreamingAudioAsync(webSocket, audioSpeechStream, request, linkedCts.Token);
        var sawClose = false;
        var emitClose = false;

        try
        {
            await foreach (var update in ReceiveStreamingTextAsync(
                    webSocket,
                    request,
                    linkedCts.Token).ConfigureAwait(false))
            {
                sawClose = update.Kind == SpeechToTextResponseUpdateKind.SessionClose;
                yield return update;

                if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdated &&
                    IsManualCommit(request.CommitStrategy))
                {
                    break;
                }
            }
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }

            emitClose = !sawClose;

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
            {
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "completed",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }
        }

        if (emitClose)
        {
            yield return new SpeechToTextResponseUpdate
            {
                Kind = SpeechToTextResponseUpdateKind.SessionClose,
                ModelId = request.ModelId
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(HttpClient))
            return _httpClient;

        if (serviceType == typeof(Func<ClientWebSocket>))
            return _webSocketFactory;

        if (serviceType == typeof(ElevenLabsSttConfig))
            return _providerConfig;

        if (serviceType == typeof(SpeechToTextClientMetadata))
            return new SpeechToTextClientMetadata(
                ElevenLabsAudioProvider.Key,
                new Uri("https://elevenlabs.io/docs/api-reference/speech-to-text/convert"),
                _defaultModelId);

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

    private Uri BuildSpeechToTextUri()
        => new($"{_baseUri.ToString().TrimEnd('/')}/speech-to-text", UriKind.Absolute);

    private Uri BuildRealtimeSpeechToTextUri(ElevenLabsRealtimeSpeechToTextRequest request)
    {
        var baseUri = ResolveWebSocketBaseUri(_providerConfig);
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/speech-to-text/realtime"
        };

        var query = new List<string>
        {
            $"model_id={Uri.EscapeDataString(request.ModelId!)}"
        };

        AddQuery(query, "include_timestamps", request.IncludeTimestamps);
        AddQuery(query, "include_language_detection", request.IncludeLanguageDetection);
        AddQuery(query, "audio_format", request.AudioFormat);
        AddQuery(query, "language_code", request.LanguageCode);
        AddQuery(query, "commit_strategy", request.CommitStrategy);
        AddQuery(query, "no_verbatim", request.NoVerbatim);
        AddQuery(query, "vad_silence_threshold_secs", request.VadSilenceThresholdSeconds);
        AddQuery(query, "vad_threshold", request.VadThreshold);
        AddQuery(query, "min_speech_duration_ms", request.MinSpeechDurationMilliseconds);
        AddQuery(query, "min_silence_duration_ms", request.MinSilenceDurationMilliseconds);
        AddQuery(query, "enable_logging", request.EnableLogging);

        if (request.Keyterms is { Length: > 0 })
        {
            foreach (var keyterm in request.Keyterms.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                query.Add($"keyterms={Uri.EscapeDataString(keyterm)}");
            }
        }

        builder.Query = string.Join('&', query);
        return builder.Uri;
    }

    private ElevenLabsSpeechToTextRequest CreateRequestModel(
        string modelId,
        string? languageCode,
        SpeechToTextOptions? options)
    {
        var request = new ElevenLabsSpeechToTextRequest
        {
            ModelId = modelId,
            LanguageCode = languageCode,
            Diarize = GetBoolOption(options, "diarize") ?? _providerConfig.Diarize,
            TagAudioEvents = GetBoolOption(options, "tagAudioEvents") ?? _providerConfig.TagAudioEvents,
            TimestampsGranularity = GetStringOption(options, "timestampsGranularity") ?? _providerConfig.TimestampsGranularity
        };

        return options?.RawRepresentationFactory?.Invoke(this) as ElevenLabsSpeechToTextRequest ?? request;
    }

    private ElevenLabsRealtimeSpeechToTextRequest CreateRealtimeRequestModel(SpeechToTextOptions? options)
    {
        var request = new ElevenLabsRealtimeSpeechToTextRequest
        {
            ModelId = FirstNonWhiteSpace(options?.ModelId, _providerConfig.RealtimeModelId, _defaultRealtimeModelId),
            LanguageCode = FirstNonWhiteSpace(options?.SpeechLanguage, _providerConfig.LanguageCode),
            SampleRate = options?.SpeechSampleRate ?? GetIntOption(options, "sampleRate"),
            AudioFormat = GetStringOption(options, "audioFormat") ?? _providerConfig.AudioFormat ?? "pcm_16000",
            CommitStrategy = GetStringOption(options, "commitStrategy") ?? _providerConfig.CommitStrategy ?? "manual",
            IncludeTimestamps = GetBoolOption(options, "includeTimestamps") ?? _providerConfig.IncludeTimestamps,
            IncludeLanguageDetection = GetBoolOption(options, "includeLanguageDetection") ?? _providerConfig.IncludeLanguageDetection,
            Keyterms = GetStringArrayOption(options, "keyterms") ?? _providerConfig.Keyterms,
            NoVerbatim = GetBoolOption(options, "noVerbatim") ?? _providerConfig.NoVerbatim,
            VadSilenceThresholdSeconds = GetDoubleOption(options, "vadSilenceThresholdSeconds") ?? _providerConfig.VadSilenceThresholdSeconds,
            VadThreshold = GetDoubleOption(options, "vadThreshold") ?? _providerConfig.VadThreshold,
            MinSpeechDurationMilliseconds = GetIntOption(options, "minSpeechDurationMilliseconds") ?? _providerConfig.MinSpeechDurationMilliseconds,
            MinSilenceDurationMilliseconds = GetIntOption(options, "minSilenceDurationMilliseconds") ?? _providerConfig.MinSilenceDurationMilliseconds,
            EnableLogging = GetBoolOption(options, "enableLogging") ?? _providerConfig.EnableLogging,
            StreamingChunkSizeBytes = GetIntOption(options, "streamingChunkSizeBytes") ?? _providerConfig.StreamingChunkSizeBytes ?? 32 * 1024,
            PreviousText = GetStringOption(options, "previousText")
        };

        return options?.RawRepresentationFactory?.Invoke(this) as ElevenLabsRealtimeSpeechToTextRequest ?? request;
    }

    private SpeechToTextResponse ToSpeechToTextResponse(
        ElevenLabsSpeechToTextResponse result,
        ElevenLabsSpeechToTextRequest requestModel)
    {
        var response = new SpeechToTextResponse(result.Text ?? string.Empty)
        {
            ModelId = requestModel.ModelId,
            RawRepresentation = result
        };

        var timedWords = result.Words?
            .Where(word => word.Start.HasValue || word.End.HasValue)
            .ToArray();
        if (timedWords is { Length: > 0 })
        {
            response.StartTime = timedWords
                .Where(word => word.Start.HasValue)
                .Select(word => TimeSpan.FromSeconds(word.Start!.Value))
                .DefaultIfEmpty()
                .Min();
            response.EndTime = timedWords
                .Where(word => word.End.HasValue)
                .Select(word => TimeSpan.FromSeconds(word.End!.Value))
                .DefaultIfEmpty()
                .Max();
        }

        response.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            ["languageCode"] = result.LanguageCode ?? requestModel.LanguageCode,
            ["languageProbability"] = result.LanguageProbability
        };

        return response;
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        if (source is MemoryStream memory && memory.TryGetBuffer(out var buffer) && buffer.Offset == 0 && buffer.Count == memory.Length)
        {
            return buffer.Array is null ? memory.ToArray() : buffer.Array[..buffer.Count];
        }

        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
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

    private static void AddStringContent(
        MultipartFormDataContent content,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            content.Add(new StringContent(value), name);
        }
    }

    private static void AddBoolContent(
        MultipartFormDataContent content,
        string name,
        bool? value)
    {
        if (value.HasValue)
        {
            content.Add(new StringContent(value.Value ? "true" : "false"), name);
        }
    }

    private static string ResolveFileName(Stream stream, SpeechToTextOptions? options)
        => FirstNonWhiteSpace(GetStringOption(options, "fileName"), stream is FileStream file ? Path.GetFileName(file.Name) : null, "audio_input")!;

    private static string ResolveContentType(SpeechToTextOptions? options)
        => FirstNonWhiteSpace(GetStringOption(options, "contentType"), "application/octet-stream")!;

    private static string? GetStringOption(SpeechToTextOptions? options, string key)
        => options?.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value as string
            : null;

    private static bool? GetBoolOption(SpeechToTextOptions? options, string key)
        => options?.AdditionalProperties?.TryGetValue(key, out var value) == true && value is bool typed
            ? typed
            : null;

    private static int? GetIntOption(SpeechToTextOptions? options, string key)
        => options?.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value switch
            {
                int typed => typed,
                long typed => checked((int)typed),
                JsonValue jsonValue when jsonValue.TryGetValue<int>(out var typed) => typed,
                _ => null
            }
            : null;

    private static double? GetDoubleOption(SpeechToTextOptions? options, string key)
        => options?.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value switch
            {
                double typed => typed,
                float typed => typed,
                decimal typed => (double)typed,
                JsonValue jsonValue when jsonValue.TryGetValue<double>(out var typed) => typed,
                _ => null
            }
            : null;

    private static string[]? GetStringArrayOption(SpeechToTextOptions? options, string key)
        => options?.AdditionalProperties?.TryGetValue(key, out var value) == true
            ? value switch
            {
                string[] typed => typed,
                IEnumerable<string> typed => typed.ToArray(),
                JsonArray jsonArray => jsonArray
                    .Select(static item => item?.GetValue<string>())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray(),
                _ => null
            }
            : null;

    private static async Task SendStreamingAudioAsync(
        ClientWebSocket webSocket,
        Stream audioSpeechStream,
        ElevenLabsRealtimeSpeechToTextRequest request,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, request.StreamingChunkSizeBytes));
        byte[]? pendingChunk = null;
        try
        {
            while (true)
            {
                var count = await audioSpeechStream.ReadAsync(buffer.AsMemory(0, request.StreamingChunkSizeBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    if (pendingChunk is not null)
                    {
                        await SendRealtimeAudioChunkAsync(
                            webSocket,
                            pendingChunk,
                            request,
                            commit: IsManualCommit(request.CommitStrategy),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }

                if (pendingChunk is not null)
                {
                    await SendRealtimeAudioChunkAsync(
                        webSocket,
                        pendingChunk,
                        request,
                        commit: false,
                        cancellationToken).ConfigureAwait(false);
                }

                pendingChunk = buffer.AsSpan(0, count).ToArray();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task SendRealtimeAudioChunkAsync(
        ClientWebSocket webSocket,
        ReadOnlyMemory<byte> audio,
        ElevenLabsRealtimeSpeechToTextRequest request,
        bool commit,
        CancellationToken cancellationToken)
    {
        var message = new ElevenLabsRealtimeInputAudioChunkMessage
        {
            AudioBase64 = Convert.ToBase64String(audio.Span),
            Commit = commit,
            SampleRate = request.SampleRate,
            PreviousText = request.PreviousText
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            message,
            ElevenLabsTtsJsonContext.Default.ElevenLabsRealtimeInputAudioChunkMessage);
        await webSocket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<SpeechToTextResponseUpdate> ReceiveStreamingTextAsync(
        ClientWebSocket webSocket,
        ElevenLabsRealtimeSpeechToTextRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (webSocket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        yield return new SpeechToTextResponseUpdate
                        {
                            Kind = SpeechToTextResponseUpdateKind.SessionClose,
                            ModelId = request.ModelId
                        };
                        yield break;
                    }

                    message.Write(buffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var update = ToStreamingTextUpdate(message.ToArray(), request.ModelId);
                if (update is not null)
                {
                    yield return update;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static SpeechToTextResponseUpdate? ToStreamingTextUpdate(
        byte[] payload,
        string? modelId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var messageType = GetString(root, "message_type");

        if (string.Equals(messageType, "session_started", StringComparison.OrdinalIgnoreCase))
        {
            return new SpeechToTextResponseUpdate
            {
                Kind = SpeechToTextResponseUpdateKind.SessionOpen,
                ResponseId = GetString(root, "session_id"),
                ModelId = modelId,
                RawRepresentation = root.Clone()
            };
        }

        if (string.Equals(messageType, "partial_transcript", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTranscriptUpdate(
                root,
                SpeechToTextResponseUpdateKind.TextUpdating,
                modelId);
        }

        if (string.Equals(messageType, "committed_transcript", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(messageType, "committed_transcript_with_timestamps", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTranscriptUpdate(
                root,
                SpeechToTextResponseUpdateKind.TextUpdated,
                modelId);
        }

        if (messageType?.Contains("error", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new SpeechToTextResponseUpdate
            {
                Kind = SpeechToTextResponseUpdateKind.Error,
                ModelId = modelId,
                RawRepresentation = root.Clone(),
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["messageType"] = messageType,
                    ["error"] = GetString(root, "error") ?? GetString(root, "message") ?? GetString(root, "detail")
                }
            };
        }

        return null;
    }

    private static SpeechToTextResponseUpdate CreateTranscriptUpdate(
        JsonElement root,
        SpeechToTextResponseUpdateKind kind,
        string? modelId)
    {
        var update = new SpeechToTextResponseUpdate(GetString(root, "text"))
        {
            Kind = kind,
            ModelId = modelId,
            RawRepresentation = root.Clone()
        };

        var languageCode = GetString(root, "language_code");
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            update.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["languageCode"] = languageCode
            };
        }

        if (root.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
        {
            var timedWords = words.EnumerateArray()
                .Where(static word => word.TryGetProperty("start", out _) || word.TryGetProperty("end", out _))
                .ToArray();

            update.StartTime = timedWords
                .Select(static word => TryGetDouble(word, "start"))
                .Where(static value => value.HasValue)
                .Select(static value => TimeSpan.FromSeconds(value!.Value))
                .DefaultIfEmpty()
                .Min();
            update.EndTime = timedWords
                .Select(static word => TryGetDouble(word, "end"))
                .Where(static value => value.HasValue)
                .Select(static value => TimeSpan.FromSeconds(value!.Value))
                .DefaultIfEmpty()
                .Max();
        }

        return update;
    }

    private static Uri ResolveWebSocketBaseUri(ElevenLabsSttConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.WebSocketBaseUrl))
        {
            return new Uri(config.WebSocketBaseUrl!, UriKind.Absolute);
        }

        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            var baseUri = new Uri(config.BaseUrl!, UriKind.Absolute);
            var scheme = baseUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                ? "ws"
                : "wss";
            return new UriBuilder(baseUri) { Scheme = scheme, Port = -1 }.Uri;
        }

        return new Uri(ElevenLabsAudioProvider.DefaultWebSocketBaseUrl, UriKind.Absolute);
    }

    private static bool IsManualCommit(string? commitStrategy)
        => string.IsNullOrWhiteSpace(commitStrategy) ||
           string.Equals(commitStrategy, "manual", StringComparison.OrdinalIgnoreCase);

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Uri.EscapeDataString(value!)}");
        }
    }

    private static void AddQuery(List<string> query, string name, bool? value)
    {
        if (value.HasValue)
        {
            query.Add($"{name}={value.Value.ToString().ToLowerInvariant()}");
        }
    }

    private static void AddQuery(List<string> query, string name, int? value)
    {
        if (value.HasValue)
        {
            query.Add($"{name}={value.Value}");
        }
    }

    private static void AddQuery(List<string> query, string name, double? value)
    {
        if (value.HasValue)
        {
            query.Add($"{name}={value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var typed)
            ? typed
            : null;

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

internal sealed class ElevenLabsSpeechToTextRequest
{
    public string? ModelId { get; set; }
    public string? LanguageCode { get; set; }
    public bool? Diarize { get; set; }
    public bool? TagAudioEvents { get; set; }
    public string? TimestampsGranularity { get; set; }
}

internal sealed class ElevenLabsRealtimeSpeechToTextRequest
{
    public string? ModelId { get; set; }
    public string? LanguageCode { get; set; }
    public int? SampleRate { get; set; }
    public string? AudioFormat { get; set; }
    public string? CommitStrategy { get; set; }
    public bool? IncludeTimestamps { get; set; }
    public bool? IncludeLanguageDetection { get; set; }
    public string[]? Keyterms { get; set; }
    public bool? NoVerbatim { get; set; }
    public double? VadSilenceThresholdSeconds { get; set; }
    public double? VadThreshold { get; set; }
    public int? MinSpeechDurationMilliseconds { get; set; }
    public int? MinSilenceDurationMilliseconds { get; set; }
    public bool? EnableLogging { get; set; }
    public int StreamingChunkSizeBytes { get; set; } = 32 * 1024;
    public string? PreviousText { get; set; }
}

internal sealed record ElevenLabsRealtimeInputAudioChunkMessage
{
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = "input_audio_chunk";

    [JsonPropertyName("audio_base_64")]
    public required string AudioBase64 { get; init; }

    [JsonPropertyName("commit")]
    public bool Commit { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("previous_text")]
    public string? PreviousText { get; init; }
}

internal sealed class ElevenLabsSpeechToTextResponse
{
    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("language_probability")]
    public double? LanguageProbability { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("words")]
    public List<ElevenLabsSpeechToTextWord>? Words { get; set; }
}

internal sealed class ElevenLabsSpeechToTextWord
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("start")]
    public double? Start { get; set; }

    [JsonPropertyName("end")]
    public double? End { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("logprob")]
    public double? LogProbability { get; set; }
}
