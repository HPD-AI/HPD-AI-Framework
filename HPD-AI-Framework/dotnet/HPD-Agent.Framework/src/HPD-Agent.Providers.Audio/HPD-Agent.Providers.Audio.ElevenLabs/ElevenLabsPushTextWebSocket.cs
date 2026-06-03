// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal sealed class ElevenLabsPushTextToSpeechStreamFactory : IPushTextToSpeechStreamFactory
{
    private readonly string _apiKey;
    private readonly ElevenLabsTtsConfig _providerConfig;
    private readonly string _defaultModelId;
    private readonly string _defaultVoiceId;
    private readonly string _defaultOutputFormat;
    private readonly Func<ClientWebSocket> _webSocketFactory;

    public ElevenLabsPushTextToSpeechStreamFactory(
        string apiKey,
        ElevenLabsTtsConfig providerConfig,
        string defaultModelId,
        string defaultVoiceId,
        string defaultOutputFormat,
        Func<ClientWebSocket>? webSocketFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _defaultModelId = defaultModelId;
        _defaultVoiceId = defaultVoiceId;
        _defaultOutputFormat = defaultOutputFormat;
        _webSocketFactory = webSocketFactory ?? (() => new ClientWebSocket());
    }

    public async ValueTask<IPushTextToSpeechStream> OpenStreamAsync(
        PushTextToSpeechStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var modelId = FirstNonWhiteSpace(request.ModelId, _defaultModelId)!;
        var voiceId = FirstNonWhiteSpace(request.VoiceId, _defaultVoiceId)!;
        var outputFormat = ElevenLabsTextToSpeechClient.NormalizeOutputFormat(
            FirstNonWhiteSpace(request.OutputFormat, _defaultOutputFormat)!);
        var webSocket = _webSocketFactory();
        webSocket.Options.SetRequestHeader("xi-api-key", _apiKey);

        var stream = new ElevenLabsPushTextToSpeechStream(
            webSocket,
            _apiKey,
            _providerConfig,
            BuildStreamInputUri(voiceId, modelId, request.Language, outputFormat),
            outputFormat,
            request.InputAggregationMode == PushTextInputAggregationMode.ProviderDefault
                ? _providerConfig.PushTextAggregationMode
                : request.InputAggregationMode);
        await stream.OpenAsync(cancellationToken).ConfigureAwait(false);
        return stream;
    }

    internal Uri BuildStreamInputUri(
        string voiceId,
        string modelId,
        string? languageCode,
        string outputFormat)
    {
        var baseUri = ResolveWebSocketBaseUri(_providerConfig);
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream-input"
        };
        var query = new List<string>
        {
            $"model_id={Uri.EscapeDataString(modelId)}",
            $"output_format={Uri.EscapeDataString(outputFormat)}"
        };

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            query.Add($"language_code={Uri.EscapeDataString(languageCode!)}");
        }

        if (!string.IsNullOrWhiteSpace(_providerConfig.ApplyTextNormalization))
        {
            query.Add($"apply_text_normalization={Uri.EscapeDataString(_providerConfig.ApplyTextNormalization!)}");
        }

        if (_providerConfig.AutoMode.HasValue)
        {
            query.Add($"auto_mode={_providerConfig.AutoMode.Value.ToString().ToLowerInvariant()}");
        }

        if (_providerConfig.SyncAlignment.HasValue)
        {
            query.Add($"sync_alignment={_providerConfig.SyncAlignment.Value.ToString().ToLowerInvariant()}");
        }

        if (_providerConfig.InactivityTimeout.HasValue)
        {
            query.Add($"inactivity_timeout={_providerConfig.InactivityTimeout.Value}");
        }

        builder.Query = string.Join('&', query);
        return builder.Uri;
    }

    private static Uri ResolveWebSocketBaseUri(ElevenLabsTtsConfig config)
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

internal sealed class ElevenLabsPushTextToSpeechStream : IPushTextToSpeechStream
{
    private readonly ClientWebSocket _webSocket;
    private readonly string _apiKey;
    private readonly ElevenLabsTtsConfig _providerConfig;
    private readonly Uri _uri;
    private readonly string _outputFormat;
    private readonly PushTextInputAggregationMode _aggregationMode;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private bool _inputCompleted;

    public ElevenLabsPushTextToSpeechStream(
        ClientWebSocket webSocket,
        string apiKey,
        ElevenLabsTtsConfig providerConfig,
        Uri uri,
        string outputFormat,
        PushTextInputAggregationMode aggregationMode)
    {
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        _apiKey = apiKey;
        _providerConfig = providerConfig ?? throw new ArgumentNullException(nameof(providerConfig));
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _outputFormat = outputFormat;
        _aggregationMode = aggregationMode;
    }

    public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        await _webSocket.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
        await SendAsync(
            new ElevenLabsWebSocketInitializeMessage
            {
                Text = " ",
                XiApiKey = _apiKey,
                VoiceSettings = CreateVoiceSettings()
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketInitializeMessage,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PushTextAsync(
        PushTextToSpeechInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (_inputCompleted)
        {
            return;
        }

        if (input.IsFinalInput)
        {
            await CompleteInputAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (input.Text.Length == 0)
        {
            return;
        }

        await SendTextAsync(input.Text, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteInputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_inputCompleted)
        {
            return;
        }

        _inputCompleted = true;
        await SendAsync(
            new ElevenLabsWebSocketTextMessage
            {
                Text = string.Empty
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketTextMessage,
            cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PushTextToSpeechAudioUpdate> ReadAudioAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (_webSocket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        yield break;
                    }

                    message.Write(buffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var payload = JsonSerializer.Deserialize(
                    message.ToArray(),
                    ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketAudioMessage);
                if (!string.IsNullOrWhiteSpace(payload?.Audio))
                {
                    yield return new PushTextToSpeechAudioUpdate
                    {
                        AudioData = Convert.FromBase64String(payload.Audio),
                        MediaType = ElevenLabsTextToSpeechClient.ToContentType(_outputFormat)
                    };
                }

                if (payload?.IsFinal == true)
                {
                    yield break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "cancelled",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "completed",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _sendGate.Dispose();
            _webSocket.Dispose();
        }
    }

    private async ValueTask SendTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var tryTriggerGeneration = _aggregationMode is not PushTextInputAggregationMode.RawDelta
            and not PushTextInputAggregationMode.Token;
        await SendAsync(
            new ElevenLabsWebSocketTextMessage
            {
                Text = text,
                TryTriggerGeneration = tryTriggerGeneration
            },
            ElevenLabsTtsJsonContext.Default.ElevenLabsWebSocketTextMessage,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendAsync<T>(
        T message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, jsonTypeInfo);
            await _webSocket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private ElevenLabsWebSocketVoiceSettings? CreateVoiceSettings()
    {
        if (_providerConfig.Stability is null &&
            _providerConfig.SimilarityBoost is null &&
            _providerConfig.Style is null &&
            _providerConfig.UseSpeakerBoost is null &&
            _providerConfig.Speed is null)
        {
            return null;
        }

        return new ElevenLabsWebSocketVoiceSettings
        {
            Stability = _providerConfig.Stability,
            SimilarityBoost = _providerConfig.SimilarityBoost,
            Style = _providerConfig.Style,
            UseSpeakerBoost = _providerConfig.UseSpeakerBoost,
            Speed = _providerConfig.Speed
        };
    }
}

internal sealed record ElevenLabsWebSocketInitializeMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = " ";

    [JsonPropertyName("xi_api_key")]
    public string? XiApiKey { get; init; }

    [JsonPropertyName("voice_settings")]
    public ElevenLabsWebSocketVoiceSettings? VoiceSettings { get; init; }
}

internal sealed record ElevenLabsWebSocketTextMessage
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("try_trigger_generation")]
    public bool? TryTriggerGeneration { get; init; }
}

internal sealed record ElevenLabsWebSocketAudioMessage
{
    [JsonPropertyName("audio")]
    public string? Audio { get; init; }

    [JsonPropertyName("isFinal")]
    public bool? IsFinal { get; init; }
}

internal sealed record ElevenLabsWebSocketVoiceSettings
{
    [JsonPropertyName("stability")]
    public double? Stability { get; init; }

    [JsonPropertyName("similarity_boost")]
    public double? SimilarityBoost { get; init; }

    [JsonPropertyName("style")]
    public double? Style { get; init; }

    [JsonPropertyName("use_speaker_boost")]
    public bool? UseSpeakerBoost { get; init; }

    [JsonPropertyName("speed")]
    public double? Speed { get; init; }
}
