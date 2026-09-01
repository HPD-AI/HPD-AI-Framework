using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Buffers.Binary;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Providers.Audio.OpenAI;

internal sealed class OpenAIRealtimeSpeechToTextParticipantFactory(
    string apiKey, Uri endpoint, string modelId, string? languageCode,
    OpenAISttOptions options, IReadOnlyDictionary<string, string>? headers = null)
    : IStreamingSpeechToTextParticipantFactory
{
    public StreamingSpeechToTextParticipantConfiguration Configuration { get; } = new()
    {
        ProviderKey = OpenAIAudioProvider.Key,
        Safety = StreamingSpeechToTextContributionSafety.Complete,
        ModelId = modelId,
        LanguageCode = languageCode,
        Keyterms = options.Keywords ?? [],
        IncludeTimestamps = false,
        IncludeLanguageDetection = string.Equals(modelId, "gpt-transcribe", StringComparison.Ordinal)
    };

    public ValueTask<IStreamingSpeechToTextParticipant> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IStreamingSpeechToTextParticipant>(
            new OpenAIRealtimeSpeechToTextParticipant(apiKey, endpoint, options, headers));
    }
}

internal sealed class OpenAIRealtimeSpeechToTextParticipant : IStreamingSpeechToTextParticipant
{
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly OpenAISttOptions _options;
    private readonly IReadOnlyDictionary<string, string>? _headers;
    private readonly Func<IOpenAIRealtimeSttSocket> _socketFactory;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private readonly object _correlationSync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IOpenAIRealtimeSttSocket? _socket;
    private StreamingSpeechToTextConnectRequest? _request;
    private StreamingSpeechToTextParticipantState _state;
    private ulong _audioSequence;
    private ulong _commitSequence;
    private ulong _observationSequence;
    private int _readerClaimed;
    private readonly Dictionary<string, StringBuilder> _partials = new(StringComparer.Ordinal);
    private bool _disposed;
    private long _resampleWeight;
    private long _weightedSampleSum;
    private PendingProviderCommit? _pendingCommit;

    private sealed class PendingProviderCommit(string operationId, ulong dispatchSequence)
    {
        internal string OperationId { get; } = operationId;
        internal ulong DispatchSequence { get; } = dispatchSequence;
        internal string? ItemId { get; set; }
    }

    internal OpenAIRealtimeSpeechToTextParticipant(string apiKey, Uri endpoint,
        OpenAISttOptions options, IReadOnlyDictionary<string, string>? headers = null,
        Func<IOpenAIRealtimeSttSocket>? socketFactory = null, TimeSpan? shutdownTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _headers = headers;
        _socketFactory = socketFactory ?? (() => new ClientWebSocketOpenAIRealtimeSttSocket());
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        if (_shutdownTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
    }

    public StreamingSpeechToTextParticipantState State
    { get { lock (_lifecycleSync) return _state; } }
    public ulong ProviderSessionEpoch { get; private set; }

    public async ValueTask<StreamingSpeechToTextReady> ConnectAsync(
        StreamingSpeechToTextConnectRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ValidateConnectRequest(request);
        ValidateOptions();
        lock (_lifecycleSync)
        {
            if (_state != StreamingSpeechToTextParticipantState.Created)
                throw new InvalidOperationException("The retained STT participant can connect exactly once.");
            _state = StreamingSpeechToTextParticipantState.Connecting;
        }
        _request = request;
        ProviderSessionEpoch = checked(ProviderSessionEpoch + 1);
        _socket = _socketFactory();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _socket.ConnectAsync(BuildUri(request.ModelId), _apiKey, _headers, linked.Token)
                .ConfigureAwait(false);
            using var created = await ReceiveDocumentAsync(linked.Token).ConfigureAwait(false);
            RequireEvent(created.RootElement, "session.created");
            await SendSessionUpdateAsync(request, linked.Token).ConfigureAwait(false);
            using var updated = await ReceiveDocumentAsync(linked.Token).ConfigureAwait(false);
            RequireEvent(updated.RootElement, "session.updated");
            ValidateUpdatedSession(updated.RootElement, request);
            var sessionId = NestedString(updated.RootElement, "session", "id") ??
                NestedString(created.RootElement, "session", "id") ?? String(created.RootElement, "event_id");
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new InvalidDataException("OpenAI realtime transcription omitted session identity.");
            SetState(StreamingSpeechToTextParticipantState.Ready);
            return new()
            {
                ProviderSessionEpoch = ProviderSessionEpoch,
                ProviderSessionId = sessionId,
                EffectiveAudioFormat = request.AudioFormat
            };
        }
        catch
        {
            SetState(StreamingSpeechToTextParticipantState.Faulted);
            throw;
        }
    }

    public async ValueTask WriteAudioAsync(StreamingSpeechToTextAudioChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(chunk);
        EnsureReady();
        ValidateChunkBound(chunk, _request!.AudioFormat);
        if (!await _sendGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The retained STT outbound capacity is occupied.");
        try
        {
            if (chunk.Sequence != checked(_audioSequence + 1))
                throw new InvalidOperationException("Audio chunks must be written in contiguous sequence order.");
            var payload = ResampleTo24Khz(chunk.Payload.Span, _request.AudioFormat.SampleRateHz);
            if (payload.Length > 0)
                await SendAsync(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "input_audio_buffer.append");
                    writer.WriteBase64String("audio", payload);
                    writer.WriteEndObject();
                }, cancellationToken).ConfigureAwait(false);
            _audioSequence = chunk.Sequence;
        }
        finally { _sendGate.Release(); }
    }

    public async ValueTask<StreamingSpeechToTextCommitDispatch> CommitAsync(
        StreamingSpeechToTextCommitRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        EnsureReady();
        if (_request!.CommitStrategy != StreamingSpeechToTextCommitStrategy.Manual)
            throw new InvalidOperationException("Explicit commit is unavailable under provider VAD.");
        if (!await _sendGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The retained STT outbound capacity is occupied.");
        try
        {
            PendingProviderCommit pending;
            lock (_correlationSync)
            {
                if (_pendingCommit is not null)
                    throw new InvalidOperationException("OpenAI retained STT permits exactly one unacknowledged commit.");
                pending = new PendingProviderCommit(request.OperationId, checked(_commitSequence + 1));
                _pendingCommit = pending;
            }
            var tail = FlushResampler();
            if (tail.Length > 0)
                await SendAsync(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "input_audio_buffer.append");
                    writer.WriteBase64String("audio", tail);
                    writer.WriteEndObject();
                }, cancellationToken).ConfigureAwait(false);
            await SendAsync(static writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("type", "input_audio_buffer.commit");
                writer.WriteEndObject();
            }, cancellationToken).ConfigureAwait(false);
            _commitSequence = pending.DispatchSequence;
            return new()
            {
                OperationId = request.OperationId,
                ProviderSessionEpoch = ProviderSessionEpoch,
                DispatchSequence = pending.DispatchSequence
            };
        }
        finally { _sendGate.Release(); }
    }

    public async IAsyncEnumerable<StreamingSpeechToTextObservation> ReadObservationsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureReady();
        if (Interlocked.Exchange(ref _readerClaimed, 1) != 0)
            throw new InvalidOperationException("A retained STT participant has exactly one observation reader.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        while (State == StreamingSpeechToTextParticipantState.Ready)
        {
            OpenAIRealtimeSttSocketMessage message = default;
            var stopped = false;
            try { message = await _socket!.ReceiveAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                stopped = true;
            }
            catch
            {
                if (State is StreamingSpeechToTextParticipantState.Stopping or StreamingSpeechToTextParticipantState.Stopped)
                    yield break;
                SetState(StreamingSpeechToTextParticipantState.Faulted);
                throw;
            }
            if (stopped)
            {
                yield return Observation(StreamingSpeechToTextObservationKind.SessionClosed, "session.stopped");
                yield break;
            }
            if (message.IsClose)
            {
                SetState(StreamingSpeechToTextParticipantState.Stopped);
                yield return Observation(StreamingSpeechToTextObservationKind.SessionClosed, "session.closed");
                yield break;
            }
            if (message.CapacityExceeded)
            {
                yield return Observation(StreamingSpeechToTextObservationKind.Error, "oversized") with
                { SafeCode = "provider-message-capacity-exceeded", EvidenceSha256 = message.EvidenceSha256 };
                continue;
            }
            StreamingSpeechToTextObservation? observation;
            try { observation = ParseObservation(message.Payload); }
            catch
            {
                SetState(StreamingSpeechToTextParticipantState.Faulted);
                throw;
            }
            if (observation is not null) yield return observation;
        }
    }

    public ValueTask<StreamingSpeechToTextUpdateDisposition> UpdateAsync(
        StreamingSpeechToTextUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReady();
        var unchanged = string.Equals(request.LanguageCode, _request!.LanguageCode,
            StringComparison.OrdinalIgnoreCase) && (request.Keyterms is null ||
            request.Keyterms.SequenceEqual(_request.Keyterms, StringComparer.Ordinal));
        // OpenAI supports session.update, but HPD does not claim it effective until
        // a correlated acknowledgement is retained. Replacement is the safe cut.
        return ValueTask.FromResult(unchanged ? StreamingSpeechToTextUpdateDisposition.Unchanged :
            StreamingSpeechToTextUpdateDisposition.ReconnectRequired);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (_state is StreamingSpeechToTextParticipantState.Created or StreamingSpeechToTextParticipantState.Stopped)
            { _state = StreamingSpeechToTextParticipantState.Stopped; return; }
            if (_state == StreamingSpeechToTextParticipantState.Stopping) return;
            _state = StreamingSpeechToTextParticipantState.Stopping;
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_shutdownTimeout);
        if (_socket is not null) await _socket.CloseAsync(timeout.Token).ConfigureAwait(false);
        if (!await _sendGate.WaitAsync(_shutdownTimeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("The retained STT send did not settle within the shutdown budget.");
        _sendGate.Release();
        SetState(StreamingSpeechToTextParticipantState.Stopped);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_socket is not null)
            {
                if (_socket.IsOpen) await _socket.CloseAsync(CancellationToken.None).AsTask()
                    .WaitAsync(_shutdownTimeout).ConfigureAwait(false);
                await _socket.DisposeAsync().AsTask().WaitAsync(_shutdownTimeout).ConfigureAwait(false);
            }
        }
        finally { _lifetime.Dispose(); SetState(StreamingSpeechToTextParticipantState.Stopped); }
    }

    private async ValueTask SendSessionUpdateAsync(StreamingSpeechToTextConnectRequest request,
        CancellationToken cancellationToken) => await SendAsync(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("type", "session.update");
        writer.WriteStartObject("session"); writer.WriteString("type", "transcription");
        writer.WriteStartObject("audio"); writer.WriteStartObject("input");
        writer.WriteStartObject("format"); writer.WriteString("type", "audio/pcm");
        writer.WriteNumber("rate", 24_000); writer.WriteEndObject();
        writer.WriteStartObject("transcription"); writer.WriteString("model", request.ModelId);
        if (!string.IsNullOrWhiteSpace(_options.Prompt)) writer.WriteString("prompt", _options.Prompt);
        var keywords = request.Keyterms.Count == 0 ? _options.Keywords ?? [] : request.Keyterms;
        if (keywords.Count > 0) { writer.WriteStartArray("keywords"); foreach (var value in keywords) writer.WriteStringValue(value); writer.WriteEndArray(); }
        if (!string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            if (request.ModelId == "gpt-live-transcribe")
            { writer.WriteStartArray("languages"); writer.WriteStringValue(request.LanguageCode); writer.WriteEndArray(); }
            else writer.WriteString("language", request.LanguageCode);
        }
        if (!string.IsNullOrWhiteSpace(_options.RealtimeDelay)) writer.WriteString("delay", _options.RealtimeDelay);
        writer.WriteEndObject(); writer.WriteNull("turn_detection"); writer.WriteEndObject();
        writer.WriteEndObject(); writer.WriteEndObject(); writer.WriteEndObject();
    }, cancellationToken, 65_536).ConfigureAwait(false);

    private async ValueTask SendAsync(Action<Utf8JsonWriter> write, CancellationToken cancellationToken,
        int maximumBytes = 262_144)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException("OpenAI realtime outbound message exceeded its capacity bound.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _socket!.SendTextAsync(stream.ToArray(), linked.Token).ConfigureAwait(false);
    }

    private async ValueTask<JsonDocument> ReceiveDocumentAsync(CancellationToken cancellationToken)
    {
        var message = await _socket!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (message.IsClose || message.CapacityExceeded)
            throw new InvalidDataException("OpenAI closed or exceeded capacity before session readiness.");
        return JsonDocument.Parse(message.Payload);
    }

    private StreamingSpeechToTextObservation? ParseObservation(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = String(root, "type") ?? "unknown";
            return type switch
            {
                "conversation.item.input_audio_transcription.delta" =>
                    Observation(StreamingSpeechToTextObservationKind.PartialTranscript, type) with
                    { Text = AppendDelta(root) },
                "conversation.item.input_audio_transcription.completed" =>
                    Observation(StreamingSpeechToTextObservationKind.CommittedTranscript, type) with
                    { Text = Complete(root), LanguageCode = FirstLanguage(root) },
                "conversation.item.input_audio_transcription.failed" or "error" =>
                    Observation(StreamingSpeechToTextObservationKind.Error, type) with
                    { SafeCode = "openai-realtime-transcription-failed",
                      Detail = Bound(NestedString(root, "error", "message")), EvidenceSha256 = Digest(payload.Span) },
                "input_audio_buffer.committed" => BindCommittedItem(root, payload.Span),
                "session.created" or "session.updated" or "rate_limits.updated" or
                    "input_audio_buffer.cleared" or "input_audio_buffer.speech_started" or
                    "input_audio_buffer.speech_stopped" => null,
                _ => Observation(StreamingSpeechToTextObservationKind.Unknown, type) with
                    { EvidenceSha256 = Digest(payload.Span) }
            };
        }
        catch (JsonException)
        {
            return Observation(StreamingSpeechToTextObservationKind.Error, "malformed") with
            { SafeCode = "malformed-provider-message", EvidenceSha256 = Digest(payload.Span) };
        }
    }

    private StreamingSpeechToTextObservation Observation(StreamingSpeechToTextObservationKind kind,
        string eventType) => new()
    {
        ProviderSessionEpoch = ProviderSessionEpoch,
        Sequence = checked(++_observationSequence), Kind = kind, ProviderEventType = eventType
    };

    private string AppendDelta(JsonElement root)
    {
        var itemId = RequireCorrelatedItem(root);
        var delta = String(root, "delta");
        if (delta is null)
            throw new InvalidDataException("OpenAI transcription delta omitted text.");
        if (!_partials.TryGetValue(itemId, out var text))
        {
            if (_partials.Count >= 8)
                throw new InvalidDataException("OpenAI transcription item residence exceeded its bound.");
            text = new StringBuilder();
            _partials.Add(itemId, text);
        }
        if (text.Length + delta.Length > 65_536)
            throw new InvalidDataException("OpenAI transcription text exceeded its bound.");
        text.Append(delta);
        return text.ToString();
    }

    private string? Complete(JsonElement root)
    {
        var itemId = RequireCorrelatedItem(root);
        var transcript = String(root, "transcript") ??
            throw new InvalidDataException("OpenAI transcription completion omitted transcript text.");
        lock (_correlationSync)
        {
            if (_pendingCommit?.ItemId != itemId)
                throw new InvalidDataException("OpenAI transcription completion lost exact commit correlation.");
            _pendingCommit = null;
        }
        _partials.Remove(itemId);
        return transcript;
    }

    private StreamingSpeechToTextObservation? BindCommittedItem(JsonElement root, ReadOnlySpan<byte> payload)
    {
        var itemId = String(root, "item_id");
        lock (_correlationSync)
        {
            if (_pendingCommit is null || _pendingCommit.ItemId is not null || string.IsNullOrWhiteSpace(itemId))
                return CorrelationError("input_audio_buffer.committed", payload);
            _pendingCommit.ItemId = itemId;
        }
        return null;
    }

    private string RequireCorrelatedItem(JsonElement root)
    {
        var itemId = String(root, "item_id");
        if (!root.TryGetProperty("content_index", out var index) || index.ValueKind != JsonValueKind.Number ||
            !index.TryGetInt32(out var contentIndex) || contentIndex != 0)
            throw new InvalidDataException("OpenAI transcription event has an unsupported content index.");
        lock (_correlationSync)
        {
            if (_pendingCommit?.ItemId is null || !string.Equals(_pendingCommit.ItemId, itemId, StringComparison.Ordinal))
                throw new InvalidDataException("OpenAI transcription event does not match the acknowledged commit item.");
        }
        return itemId!;
    }

    private StreamingSpeechToTextObservation CorrelationError(string eventType, ReadOnlySpan<byte> payload) =>
        Observation(StreamingSpeechToTextObservationKind.Error, eventType) with
        { SafeCode = "openai-realtime-correlation-failed", EvidenceSha256 = Digest(payload) };

    private Uri BuildUri(string modelId)
    {
        var builder = new UriBuilder(_endpoint);
        builder.Scheme = builder.Scheme == Uri.UriSchemeHttps ? "wss" :
            builder.Scheme == Uri.UriSchemeHttp ? "ws" : builder.Scheme;
        builder.Path = builder.Path.TrimEnd('/') + "/realtime";
        builder.Query = $"model={Uri.EscapeDataString(modelId)}";
        return builder.Uri;
    }

    private static void ValidateConnectRequest(StreamingSpeechToTextConnectRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        if (request.ModelId is not ("gpt-live-transcribe" or "gpt-transcribe"))
            throw new NotSupportedException(
                "OpenAI retained STT requires gpt-live-transcribe or gpt-transcribe.");
        if (request.CommitStrategy != StreamingSpeechToTextCommitStrategy.Manual)
            throw new NotSupportedException("OpenAI retained STT currently requires HPD manual commit authority.");
        if (request.AudioFormat.SampleRateHz is < 8_000 or > 192_000 || request.AudioFormat.ChannelCount != 1 ||
            request.AudioFormat.BitsPerSample != 16 ||
            !string.Equals(request.AudioFormat.Encoding, "pcm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("OpenAI retained STT requires mono PCM16 input between 8 and 192 kHz.");
        if (request.Keyterms.Count > 100 || request.Keyterms.Sum(static value => value.Length) > 10_000 ||
            request.Keyterms.Any(static value => value.Length > 100 ||
            value.IndexOfAny(['<', '>', '\r', '\n']) >= 0))
            throw new ArgumentOutOfRangeException(nameof(request), "OpenAI realtime keywords are invalid or unbounded.");
    }

    private void ValidateOptions()
    {
        if (_options.Prompt?.Length > 8_192)
            throw new ArgumentOutOfRangeException(nameof(_options), "OpenAI realtime prompt exceeds its capacity bound.");
        if (_options.Keywords is { } keywords && (keywords.Length > 100 ||
            keywords.Sum(static value => value.Length) > 10_000 ||
            keywords.Any(static value => value.Length > 100 || value.IndexOfAny(['<', '>', '\r', '\n']) >= 0)))
            throw new ArgumentOutOfRangeException(nameof(_options), "OpenAI realtime keywords are invalid or unbounded.");
        if (_options.RealtimeDelay is { } delay && delay is not ("minimal" or "low" or "medium" or "high" or "xhigh"))
            throw new ArgumentOutOfRangeException(nameof(_options), "OpenAI realtime delay is unsupported.");
    }

    private void ValidateUpdatedSession(JsonElement root, StreamingSpeechToTextConnectRequest request)
    {
        if (!root.TryGetProperty("session", out var session) ||
            String(session, "type") != "transcription" ||
            !TryNested(session, out var input, "audio", "input") ||
            !TryNested(input, out var format, "format") || String(format, "type") != "audio/pcm" ||
            !format.TryGetProperty("rate", out var rate) || !rate.TryGetInt32(out var rateValue) || rateValue != 24_000 ||
            !TryNested(input, out var transcription, "transcription") ||
            String(transcription, "model") != request.ModelId ||
            !input.TryGetProperty("turn_detection", out var turnDetection) ||
            turnDetection.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("OpenAI did not acknowledge the requested retained transcription session.");
        if (!string.IsNullOrWhiteSpace(request.LanguageCode))
        {
            var languageMatches = request.ModelId == "gpt-live-transcribe"
                ? transcription.TryGetProperty("languages", out var languages) &&
                  languages.ValueKind == JsonValueKind.Array && languages.GetArrayLength() == 1 &&
                  languages[0].ValueKind == JsonValueKind.String && languages[0].GetString() == request.LanguageCode
                : String(transcription, "language") == request.LanguageCode;
            if (!languageMatches)
                throw new InvalidDataException("OpenAI did not acknowledge the requested transcription language.");
        }
        if (!string.IsNullOrWhiteSpace(_options.Prompt) && String(transcription, "prompt") != _options.Prompt)
            throw new InvalidDataException("OpenAI did not acknowledge the requested transcription prompt.");
        var expectedKeywords = request.Keyterms.Count == 0 ? _options.Keywords ?? [] : request.Keyterms;
        if (expectedKeywords.Count > 0 && (!transcription.TryGetProperty("keywords", out var keywords) ||
            keywords.ValueKind != JsonValueKind.Array ||
            !keywords.EnumerateArray().Select(static value => value.GetString()).SequenceEqual(expectedKeywords)))
            throw new InvalidDataException("OpenAI did not acknowledge the requested transcription keywords.");
        if (!string.IsNullOrWhiteSpace(_options.RealtimeDelay) &&
            String(transcription, "delay") != _options.RealtimeDelay)
            throw new InvalidDataException("OpenAI did not acknowledge the requested transcription delay.");
    }

    private static void ValidateChunkBound(StreamingSpeechToTextAudioChunk chunk,
        StreamingSpeechToTextAudioFormat format)
    {
        var bytesPerSecond = checked(format.SampleRateHz * format.ChannelCount * format.BitsPerSample / 8);
        if (chunk.Payload.Length > bytesPerSecond)
            throw new ArgumentOutOfRangeException(nameof(chunk), "One wire audio item cannot exceed one second.");
    }

    private byte[] ResampleTo24Khz(ReadOnlySpan<byte> input, int inputRate)
    {
        if ((input.Length & 1) != 0) throw new InvalidDataException("PCM16 input must be sample-aligned.");
        var maximumSamples = checked((int)((_resampleWeight + (long)(input.Length / 2) * 24_000) / inputRate));
        var output = new byte[checked(maximumSamples * sizeof(short))];
        var written = 0;
        for (var offset = 0; offset < input.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(input[offset..]);
            var remaining = 24_000L;
            while (remaining > 0)
            {
                var accepted = Math.Min(remaining, inputRate - _resampleWeight);
                _weightedSampleSum += sample * accepted;
                _resampleWeight += accepted;
                remaining -= accepted;
                if (_resampleWeight != inputRate) continue;
                var rounded = _weightedSampleSum >= 0
                    ? (_weightedSampleSum + inputRate / 2) / inputRate
                    : (_weightedSampleSum - inputRate / 2) / inputRate;
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(written),
                    checked((short)Math.Clamp(rounded, short.MinValue, short.MaxValue)));
                written += sizeof(short);
                _resampleWeight = 0;
                _weightedSampleSum = 0;
            }
        }
        return written == output.Length ? output : output[..written];
    }

    private byte[] FlushResampler()
    {
        if (_resampleWeight == 0) return [];
        var rounded = _weightedSampleSum >= 0
            ? (_weightedSampleSum + _resampleWeight / 2) / _resampleWeight
            : (_weightedSampleSum - _resampleWeight / 2) / _resampleWeight;
        var output = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(output,
            checked((short)Math.Clamp(rounded, short.MinValue, short.MaxValue)));
        _resampleWeight = 0;
        _weightedSampleSum = 0;
        return output;
    }

    private static void RequireEvent(JsonElement root, string expected)
    {
        var actual = String(root, "type");
        if (actual == "error") throw new InvalidDataException(Bound(NestedString(root, "error", "message")) ?? "OpenAI session failed.");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"OpenAI expected {expected} before retained readiness.");
    }
    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? NestedString(JsonElement root, string parent, string name) =>
        root.TryGetProperty(parent, out var value) && value.ValueKind == JsonValueKind.Object ? String(value, name) : null;
    private static bool TryNested(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (var name in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value))
                return false;
        }
        return true;
    }
    private static string? FirstLanguage(JsonElement root) =>
        root.TryGetProperty("languages", out var values) && values.ValueKind == JsonValueKind.Array &&
        values.GetArrayLength() > 0 ? String(values[0], "code") : null;
    private static string? Bound(string? value) => value is null ? null : value[..Math.Min(value.Length, 1024)];
    private static string Digest(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    private void EnsureReady()
    {
        if (State != StreamingSpeechToTextParticipantState.Ready || _socket is null || !_socket.IsOpen)
            throw new InvalidOperationException("The retained STT participant is not ready.");
    }
    private void SetState(StreamingSpeechToTextParticipantState state)
    { lock (_lifecycleSync) _state = state; }
}
