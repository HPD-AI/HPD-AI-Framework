using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal sealed class ElevenLabsRealtimeSpeechToTextParticipant : IStreamingSpeechToTextParticipant
{
    private const int MaximumProviderMessageBytes = 256 * 1024;
    private static readonly HashSet<string> KnownErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "auth_error", "quota_exceeded", "quota_exceeded_error", "transcriber_error",
        "input_error", "commit_throttled", "unaccepted_terms_error", "rate_limited", "queue_overflow",
        "resource_exhausted", "session_time_limit_exceeded", "chunk_size_exceeded",
        "insufficient_audio_activity"
    };

    private readonly string _apiKey;
    private readonly ElevenLabsSttRuntimeSettings _settings;
    private readonly Func<IElevenLabsRealtimeSttSocket> _socketFactory;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private IElevenLabsRealtimeSttSocket? _socket;
    private StreamingSpeechToTextConnectRequest? _request;
    private StreamingSpeechToTextReady? _ready;
    private ulong _audioSequence;
    private ulong _commitSequence;
    private ulong _observationSequence;
    private int _readerClaimed;
    private bool _firstAudioChunk = true;
    private bool _disposed;

    internal ElevenLabsRealtimeSpeechToTextParticipant(
        string apiKey,
        ElevenLabsSttRuntimeSettings settings,
        Func<IElevenLabsRealtimeSttSocket>? socketFactory = null,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _socketFactory = socketFactory ?? (() => new ClientWebSocketRealtimeSttSocket());
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        if (_shutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
    }

    private StreamingSpeechToTextParticipantState _state;

    public StreamingSpeechToTextParticipantState State
    {
        get { lock (_lifecycleSync) return _state; }
    }

    public ulong ProviderSessionEpoch { get; private set; }

    public async ValueTask<StreamingSpeechToTextReady> ConnectAsync(
        StreamingSpeechToTextConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ValidateConnectRequest(request);
        lock (_lifecycleSync)
        {
            if (_state != StreamingSpeechToTextParticipantState.Created)
                throw new InvalidOperationException("The retained STT participant can connect exactly once.");
            _state = StreamingSpeechToTextParticipantState.Connecting;
        }
        var socket = _socketFactory();
        _socket = socket;
        _request = request;
        ProviderSessionEpoch = checked(ProviderSessionEpoch + 1);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            await socket.ConnectAsync(BuildUri(request), _apiKey, linkedCts.Token).ConfigureAwait(false);
            var first = await socket.ReceiveAsync(linkedCts.Token).ConfigureAwait(false);
            if (first.IsClose)
                throw new InvalidDataException("ElevenLabs closed before session readiness.");
            if (first.CapacityExceeded)
                throw new InvalidDataException("ElevenLabs readiness message exceeded the bounded limit.");

            _ready = ParseReady(first.Payload, request, ProviderSessionEpoch);
            lock (_lifecycleSync)
            {
                if (_state != StreamingSpeechToTextParticipantState.Connecting)
                    throw new OperationCanceledException("The participant stopped before readiness.");
                _state = StreamingSpeechToTextParticipantState.Ready;
            }
            return _ready;
        }
        catch
        {
            lock (_lifecycleSync)
            {
                if (_state is not (StreamingSpeechToTextParticipantState.Stopping or StreamingSpeechToTextParticipantState.Stopped))
                    _state = StreamingSpeechToTextParticipantState.Faulted;
            }
            throw;
        }
    }

    public async ValueTask WriteAudioAsync(
        StreamingSpeechToTextAudioChunk chunk,
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
            EnsureReady();
            if (chunk.Sequence != checked(_audioSequence + 1))
                throw new InvalidOperationException("Audio chunks must be written in contiguous sequence order.");
            var message = new ElevenLabsRealtimeInputAudioChunkMessage
            {
                AudioBase64 = Convert.ToBase64String(chunk.Payload.Span),
                Commit = false,
                SampleRate = _request!.AudioFormat.SampleRateHz,
                PreviousText = _firstAudioChunk ? NormalizePreviousText(_request.PreviousText) : null
            };
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            await SendAsync(message, linkedCts.Token).ConfigureAwait(false);
            _audioSequence = chunk.Sequence;
            _firstAudioChunk = false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask<StreamingSpeechToTextCommitDispatch> CommitAsync(
        StreamingSpeechToTextCommitRequest request,
        CancellationToken cancellationToken = default)
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
            EnsureReady();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            await SendAsync(
                new ElevenLabsRealtimeInputAudioChunkMessage
                {
                    AudioBase64 = string.Empty,
                    Commit = true,
                    SampleRate = _request.AudioFormat.SampleRateHz
                },
                linkedCts.Token).ConfigureAwait(false);
            var sequence = checked(++_commitSequence);
            return new StreamingSpeechToTextCommitDispatch
            {
                OperationId = request.OperationId,
                ProviderSessionEpoch = ProviderSessionEpoch,
                DispatchSequence = sequence,
                Outcome = StreamingSpeechToTextCommitDispatchOutcome.DispatchedOutcomeUnknown
            };
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async IAsyncEnumerable<StreamingSpeechToTextObservation> ReadObservationsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureReady();
        if (Interlocked.Exchange(ref _readerClaimed, 1) != 0)
            throw new InvalidOperationException("A retained STT participant has exactly one observation reader.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCts.Token);
        while (State == StreamingSpeechToTextParticipantState.Ready)
        {
            ElevenLabsRealtimeSttSocketMessage message = default;
            var stopped = false;
            try
            {
                message = await _socket!.ReceiveAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                stopped = true;
            }
            catch
            {
                if (State is StreamingSpeechToTextParticipantState.Stopping or StreamingSpeechToTextParticipantState.Stopped)
                {
                    stopped = true;
                }
                else
                {
                    SetState(StreamingSpeechToTextParticipantState.Faulted);
                    throw;
                }
            }

            if (stopped)
            {
                yield return NewObservation(StreamingSpeechToTextObservationKind.SessionClosed, "session_stopped");
                yield break;
            }

            if (message.IsClose)
            {
                SetState(StreamingSpeechToTextParticipantState.Stopped);
                yield return NewObservation(StreamingSpeechToTextObservationKind.SessionClosed, "session_closed");
                yield break;
            }

            if (message.CapacityExceeded)
            {
                yield return NewObservation(
                    StreamingSpeechToTextObservationKind.Error,
                    "provider_message_capacity_exceeded") with
                {
                    SafeCode = "provider-message-capacity-exceeded",
                    EvidenceSha256 = message.EvidenceSha256
                };
                continue;
            }

            yield return ParseObservation(message.Payload);
        }
    }

    public ValueTask<StreamingSpeechToTextUpdateDisposition> UpdateAsync(
        StreamingSpeechToTextUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReady();

        var unchanged = string.Equals(request.LanguageCode, _request!.LanguageCode, StringComparison.OrdinalIgnoreCase) &&
                        (request.Keyterms is null || request.Keyterms.SequenceEqual(_request.Keyterms, StringComparer.Ordinal));
        return ValueTask.FromResult(unchanged
            ? StreamingSpeechToTextUpdateDisposition.Unchanged
            : StreamingSpeechToTextUpdateDisposition.ReconnectRequired);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (_state is StreamingSpeechToTextParticipantState.Stopped or StreamingSpeechToTextParticipantState.Created)
            {
                _state = StreamingSpeechToTextParticipantState.Stopped;
                return;
            }
            if (_state == StreamingSpeechToTextParticipantState.Stopping)
                return;
            _state = StreamingSpeechToTextParticipantState.Stopping;
        }

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        using var settlementCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        settlementCts.CancelAfter(_shutdownTimeout);
        try
        {
            if (_socket is not null)
                await _socket.CloseAsync(settlementCts.Token).AsTask()
                    .WaitAsync(_shutdownTimeout, cancellationToken).ConfigureAwait(false);
            if (!await _sendGate.WaitAsync(_shutdownTimeout, cancellationToken).ConfigureAwait(false))
                throw new TimeoutException("The retained STT send did not settle within the shutdown budget.");
            _sendGate.Release();
            SetState(StreamingSpeechToTextParticipantState.Stopped);
        }
        catch
        {
            SetState(StreamingSpeechToTextParticipantState.Faulted);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_socket is not null)
            {
                if (_socket.IsOpen)
                    await _socket.CloseAsync(CancellationToken.None).AsTask().WaitAsync(_shutdownTimeout).ConfigureAwait(false);
                await _socket.DisposeAsync().AsTask().WaitAsync(_shutdownTimeout).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifetimeCts.Dispose();
            SetState(StreamingSpeechToTextParticipantState.Stopped);
        }
    }

    private async ValueTask SendAsync(
        ElevenLabsRealtimeInputAudioChunkMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            ElevenLabsTtsJsonContext.Default.ElevenLabsRealtimeInputAudioChunkMessage);
        await _socket!.SendTextAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private StreamingSpeechToTextObservation ParseObservation(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaximumProviderMessageBytes)
        {
            return new StreamingSpeechToTextObservation
            {
                ProviderSessionEpoch = ProviderSessionEpoch,
                Sequence = checked(++_observationSequence),
                Kind = StreamingSpeechToTextObservationKind.Error,
                ProviderEventType = "oversized",
                SafeCode = "provider-message-capacity-exceeded",
                EvidenceSha256 = Digest(payload.Span)
            };
        }
        try
        {
            return ParseObservationCore(payload);
        }
        catch (JsonException)
        {
            return new StreamingSpeechToTextObservation
            {
                ProviderSessionEpoch = ProviderSessionEpoch,
                Sequence = checked(++_observationSequence),
                Kind = StreamingSpeechToTextObservationKind.Error,
                ProviderEventType = "malformed",
                SafeCode = "malformed-provider-message",
                EvidenceSha256 = Digest(payload.Span)
            };
        }
    }

    private StreamingSpeechToTextObservation ParseObservationCore(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventType = GetString(root, "message_type") ?? "unknown";
        var kind = eventType.ToLowerInvariant() switch
        {
            "partial_transcript" => StreamingSpeechToTextObservationKind.PartialTranscript,
            "final_transcript" => StreamingSpeechToTextObservationKind.FinalTranscript,
            "final_transcript_with_timestamps" => StreamingSpeechToTextObservationKind.FinalTranscriptWithTimestamps,
            "committed_transcript" => StreamingSpeechToTextObservationKind.CommittedTranscript,
            "committed_transcript_with_timestamps" => StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps,
            _ when KnownErrors.Contains(eventType) => StreamingSpeechToTextObservationKind.Error,
            _ => StreamingSpeechToTextObservationKind.Unknown
        };

        return new StreamingSpeechToTextObservation
        {
            ProviderSessionEpoch = ProviderSessionEpoch,
            Sequence = checked(++_observationSequence),
            Kind = kind,
            ProviderEventType = eventType,
            Text = GetString(root, "text"),
            LanguageCode = GetString(root, "language_code"),
            SafeCode = kind == StreamingSpeechToTextObservationKind.Error ? eventType : null,
            Detail = kind == StreamingSpeechToTextObservationKind.Error
                ? BoundDetail(GetString(root, "error") ?? GetString(root, "message") ?? GetString(root, "detail"))
                : null,
            EvidenceSha256 = kind is StreamingSpeechToTextObservationKind.Error or StreamingSpeechToTextObservationKind.Unknown
                ? Digest(payload.Span)
                : null,
            WordTimings = ParseWordTimings(root)
        };
    }

    private StreamingSpeechToTextObservation NewObservation(
        StreamingSpeechToTextObservationKind kind,
        string providerEventType) => new()
        {
            ProviderSessionEpoch = ProviderSessionEpoch,
            Sequence = checked(++_observationSequence),
            Kind = kind,
            ProviderEventType = providerEventType
        };

    private static StreamingSpeechToTextReady ParseReady(
        ReadOnlyMemory<byte> payload,
        StreamingSpeechToTextConnectRequest request,
        ulong epoch)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!string.Equals(GetString(root, "message_type"), "session_started", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ElevenLabs did not emit session_started as its first message.");
        var sessionId = GetString(root, "session_id");
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidDataException("ElevenLabs session_started omitted session_id.");

        return new StreamingSpeechToTextReady
        {
            ProviderSessionEpoch = epoch,
            ProviderSessionId = sessionId,
            EffectiveAudioFormat = request.AudioFormat
        };
    }

    private Uri BuildUri(StreamingSpeechToTextConnectRequest request)
    {
        return ElevenLabsRealtimeSpeechToTextProtocol.BuildUri(_settings, new ElevenLabsRealtimeSpeechToTextRequest
        {
            ModelId = request.ModelId,
            AudioFormat = ToProviderAudioFormat(request.AudioFormat),
            SampleRate = request.AudioFormat.SampleRateHz,
            CommitStrategy = request.CommitStrategy == StreamingSpeechToTextCommitStrategy.Manual ? "manual" : "vad",
            IncludeTimestamps = request.IncludeTimestamps,
            IncludeLanguageDetection = request.IncludeLanguageDetection,
            LanguageCode = request.LanguageCode,
            Keyterms = request.Keyterms.ToArray(),
            NoVerbatim = _settings.NoVerbatim,
            VadSilenceThresholdSeconds = _settings.VadSilenceThresholdSeconds,
            VadThreshold = _settings.VadThreshold,
            MinSpeechDurationMilliseconds = _settings.MinSpeechDurationMilliseconds,
            MinSilenceDurationMilliseconds = _settings.MinSilenceDurationMilliseconds,
            EnableLogging = _settings.EnableLogging
        });
    }

    private static void ValidateConnectRequest(StreamingSpeechToTextConnectRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        ArgumentNullException.ThrowIfNull(request.AudioFormat);
        if (request.AudioFormat.ChannelCount != 1 || request.AudioFormat.BitsPerSample != 16 ||
            !string.Equals(request.AudioFormat.Encoding, "pcm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("ElevenLabs retained STT requires mono PCM16 input.");
        _ = ToProviderAudioFormat(request.AudioFormat);
        if (request.PreviousText is { Length: > 50 })
            throw new ArgumentOutOfRangeException(nameof(request), "Previous text is bounded to 50 characters.");
        if (request.Keyterms.Count > 50 || request.Keyterms.Any(static value => value.Length > 50))
            throw new ArgumentOutOfRangeException(nameof(request), "Realtime keyterms are bounded to 50 values of 50 characters.");
    }

    private static void ValidateChunkBound(
        StreamingSpeechToTextAudioChunk chunk,
        StreamingSpeechToTextAudioFormat format)
    {
        var bytesPerSecond = checked(format.SampleRateHz * format.ChannelCount * format.BitsPerSample / 8);
        if (chunk.Payload.Length > bytesPerSecond)
            throw new ArgumentOutOfRangeException(nameof(chunk), "One wire audio item cannot exceed one second.");
    }

    private static string ToProviderAudioFormat(StreamingSpeechToTextAudioFormat format) => format.SampleRateHz switch
    {
        8000 => "pcm_8000",
        16000 => "pcm_16000",
        22050 => "pcm_22050",
        24000 => "pcm_24000",
        44100 => "pcm_44100",
        48000 => "pcm_48000",
        _ => throw new NotSupportedException("The PCM sample rate is not supported by ElevenLabs realtime STT.")
    };

    private static IReadOnlyList<StreamingSpeechToTextWordTiming> ParseWordTimings(JsonElement root)
    {
        if (!root.TryGetProperty("words", out var words) || words.ValueKind != JsonValueKind.Array)
            return Array.Empty<StreamingSpeechToTextWordTiming>();

        var result = new List<StreamingSpeechToTextWordTiming>();
        foreach (var word in words.EnumerateArray())
        {
            var text = GetString(word, "text");
            if (string.IsNullOrEmpty(text))
                continue;
            result.Add(new StreamingSpeechToTextWordTiming
            {
                Text = text,
                Start = GetSeconds(word, "start"),
                End = GetSeconds(word, "end")
            });
        }
        return result.ToArray();
    }

    private static string? GetString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static TimeSpan? GetSeconds(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string? NormalizePreviousText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? BoundDetail(string? value) => value is null
        ? null
        : value[..Math.Min(value.Length, 1024)];

    private static string Digest(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private void EnsureReady()
    {
        if (State != StreamingSpeechToTextParticipantState.Ready || _socket is null || !_socket.IsOpen)
            throw new InvalidOperationException("The retained STT participant is not ready.");
    }

    private void SetState(StreamingSpeechToTextParticipantState state)
    {
        lock (_lifecycleSync)
            _state = state;
    }
}
