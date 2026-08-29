using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Audio.LiveKit.Generated;
using HPD.Agent.Audio.Output;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.LiveKit;

/// <summary>Concrete managed-session backend over the qualified LiveKit FFI leaf.</summary>
public sealed class LiveKitManagedAudioSessionBackend : IManagedAudioSessionBackendV1
{
    private readonly LiveKitManagedAudioSessionBackendOptions _options;
    private readonly Func<LiveKitFfiHost> _host;

    public LiveKitManagedAudioSessionBackend(LiveKitManagedAudioSessionBackendOptions options)
        : this(options, static () => LiveKitFfiHost.ProcessGlobal(new LiveKitFfiWireDecoder())) { }

    internal LiveKitManagedAudioSessionBackend(
        LiveKitManagedAudioSessionBackendOptions options,
        Func<LiveKitFfiHost> host)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("wss" or "https" or "ws"))
            throw new ArgumentException("LiveKit endpoint must be an absolute ws, wss, or https URI.", nameof(options));
        if (options.InboundFrameCapacity is <= 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.AudioSampleRateHz is < 8_000 or > 192_000)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Managed LiveKit audio sample rate must be between 8 kHz and 192 kHz.");
    }

    public async ValueTask<IManagedAudioSessionV1> StartAsync(
        ManagedAudioSessionStartRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = LiveKitAudioTransport.Decode(request.Bindings);
        ValidateBinding(binding);
        if (_options.VerifyNativeArtifact)
        {
            var artifact = LiveKitRuntimeSupport.VerifyCurrentArtifact();
            if (artifact.Disposition != LiveKitNativeArtifactDisposition.Verified)
                throw new PlatformNotSupportedException($"LiveKit native runtime is unavailable: {artifact.SafeCode}.");
        }

        var credential = await _options.CredentialResolver(request, cancellationToken).ConfigureAwait(false);
        if (credential is null || credential.Length == 0)
            throw new InvalidDataException("LiveKit credential resolver returned no credential.");
        char[]? token = null;
        try
        {
            token = LiveKitParticipantToken.Resolve(credential, _options.Transport, binding, DateTimeOffset.UtcNow);
            var host = _host();
            var format = new AudioFormat
            {
                SampleRate = _options.AudioSampleRateHz,
                ChannelCount = 1,
                SampleFormat = AudioSampleFormat.Pcm16
            };
            return await ConnectAsync(host, request, binding, token, format, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(credential);
            if (token is not null) Array.Clear(token);
        }
    }

    private async ValueTask<IManagedAudioSessionV1> ConnectAsync(
        LiveKitFfiHost host,
        ManagedAudioSessionStartRequestV1 request,
        LiveKitAudioSessionBinding binding,
        char[] token,
        AudioFormat format,
        CancellationToken cancellationToken)
    {
        var connected = await host.IssueTrackedAsync(
            LiveKitFfiOperation.Connect,
            LiveKitFfiProtocolCodec.Connect(_options.Endpoint, token, _options.Transport),
            TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        if (connected.Unknown is not null)
            throw new ManagedAudioSessionStartOutcomeUnknownException(connected.Unknown.Value.ToString());

        LiveKitNativeHandleOwner? room = null;
        LiveKitNativeHandleOwner? participant = null;
        LiveKitNativeHandleOwner? source = null;
        LiveKitNativeHandleOwner? track = null;
        LiveKitNativeHandleOwner? publication = null;
        IDisposable? roomRegistration = null;
        try
        {
            var handles = LiveKitFfiProtocolCodec.DecodeConnectCompletion(connected.Completion!.Value.Bytes);
            room = host.Own(LiveKitFfiHandleKind.Room, handles.Room);
            participant = host.Own(LiveKitFfiHandleKind.Participant, handles.Participant);
            var deferredRoom = new DeferredRoomSink();
            roomRegistration = host.RegisterRoom(room.Value, deferredRoom);
            host.InvokeSynchronous(LiveKitFfiOperation.ReadyForRoomEvent,
                LiveKitFfiProtocolCodec.Ready(room.Value), cancellationToken);

            var sourceResponse = host.InvokeSynchronous(LiveKitFfiOperation.NewAudioSource,
                LiveKitFfiProtocolCodec.NewAudioSource(format.SampleRate, format.ChannelCount), cancellationToken);
            source = host.Own(LiveKitFfiHandleKind.AudioSource,
                LiveKitFfiProtocolCodec.DecodeOwnedHandleResponse(sourceResponse.Bytes, 26, "audio source response"));
            var trackResponse = host.InvokeSynchronous(LiveKitFfiOperation.CreateAudioTrack,
                LiveKitFfiProtocolCodec.CreateAudioTrack($"hpd-{Guid.NewGuid():N}", source.Value), cancellationToken);
            track = host.Own(LiveKitFfiHandleKind.Track,
                LiveKitFfiProtocolCodec.DecodeOwnedHandleResponse(trackResponse.Bytes, 16, "audio track response"));
            var published = await host.IssueTrackedAsync(LiveKitFfiOperation.PublishTrack,
                LiveKitFfiProtocolCodec.PublishTrack(participant.Value, track.Value),
                TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            if (published.Unknown is not null)
                throw new ManagedAudioSessionStartOutcomeUnknownException(published.Unknown.Value.ToString());
            var publishedIdentity = LiveKitFfiProtocolCodec.DecodePublishCompletion(published.Completion!.Value.Bytes);
            publication = host.Own(LiveKitFfiHandleKind.TrackPublication, publishedIdentity.Handle);

            var audioSessionId = $"audio-{Guid.NewGuid():N}";
            var control = new SessionControl(
                host, room.Value, participant.Value, source.Value, publishedIdentity.Sid, format);
            var owners = new LiveKitRoomMediaOwner(room, participant, null, source, track, publication);
            var runtime = new LiveKitTransportSessionRuntime(
                audioSessionId, format, control, owners, new LiveKitSessionGenerationFence(),
                _options.InboundFrameCapacity);
            var session = new LiveKitManagedAudioSession(runtime, _options.TranscriptSource);
            var roomSink = new RoomSink(host, runtime, owners, format, binding.RemoteParticipantIdentity);
            owners.AddRegistration(roomRegistration);
            deferredRoom.Attach(roomSink);
            room = participant = source = track = publication = null;
            roomRegistration = null;
            return session;
        }
        catch
        {
            roomRegistration?.Dispose();
            await DisposePartialAsync(publication, track, source, participant, room).ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateBinding(LiveKitAudioSessionBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.RoomName) || binding.RoomName.Length > 256 ||
            string.IsNullOrWhiteSpace(binding.ParticipantIdentity) || binding.ParticipantIdentity.Length > 256 ||
            binding.RemoteParticipantIdentity is { Length: > 256 })
            throw new InvalidDataException("LiveKit room and participant identities are required and bounded.");
    }

    private static async ValueTask DisposePartialAsync(params LiveKitNativeHandleOwner?[] owners)
    {
        foreach (var owner in owners)
            if (owner is not null && !owner.IsReleased)
                try { await owner.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    private sealed class SessionControl(
        LiveKitFfiHost host,
        ulong room,
        ulong participant,
        ulong source,
        string publicationSid,
        AudioFormat format) : ILiveKitSessionControlPort
    {
        private readonly object _gate = new();
        private readonly Queue<LiveKitFfiCompletionKey> _unknownCaptures = [];
        private LiveKitFfiCompletionKey? _unknownUnpublish;
        private LiveKitFfiCompletionKey? _unknownDisconnect;

        public async ValueTask CaptureAsync(AudioFrame frame, CancellationToken cancellationToken)
        {
            using var pin = frame.Data.Pin();
            var request = LiveKitFfiProtocolCodec.CaptureAudioFrame(
                source, Pointer(pin), format.ChannelCount, format.SampleRate, frame.SamplesPerChannel);
            var completed = await host.IssueTrackedAsync(
                LiveKitFfiOperation.CaptureAudioFrame, request,
                TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            if (completed.Unknown is { } unknown)
            {
                lock (_gate) _unknownCaptures.Enqueue(unknown);
                throw new InvalidDataException("LiveKit audio capture outcome is unknown.");
            }
            LiveKitFfiProtocolCodec.DecodeOperationSuccess(
                completed.Completion!.Value.Bytes, 13, "capture callback");
        }

        private static unsafe nint Pointer(MemoryHandle pin) => (nint)pin.Pointer;

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            host.InvokeSynchronous(LiveKitFfiOperation.ClearAudioBuffer,
                LiveKitFfiProtocolCodec.ClearAudioBuffer(source), cancellationToken);
            return ValueTask.CompletedTask;
        }

        public async ValueTask UnpublishAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
                if (_unknownCaptures.Count != 0)
                    throw new InvalidDataException("LiveKit capture reconciliation is incomplete.");
            var outcome = await host.IssueTrackedAsync(LiveKitFfiOperation.UnpublishTrack,
                LiveKitFfiProtocolCodec.UnpublishTrack(participant, publicationSid),
                TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            if (outcome.Unknown is { } unknown)
            {
                lock (_gate) _unknownUnpublish = unknown;
                throw new InvalidDataException("LiveKit unpublish outcome is unknown.");
            }
            LiveKitFfiProtocolCodec.DecodeOperationSuccess(
                outcome.Completion!.Value.Bytes, 10, "unpublish callback");
        }

        public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            var outcome = await host.IssueTrackedAsync(LiveKitFfiOperation.Disconnect,
                LiveKitFfiProtocolCodec.Disconnect(room), TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            if (outcome.Unknown is { } unknown)
            {
                lock (_gate) _unknownDisconnect = unknown;
                throw new InvalidDataException("LiveKit disconnect outcome is unknown.");
            }
            LiveKitFfiProtocolCodec.DecodeOperationSuccess(
                outcome.Completion!.Value.Bytes, 7, "disconnect callback");
        }

        public async ValueTask<bool> ReconcileStopAsync(string operationId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                while (_unknownCaptures.TryPeek(out var key) && host.TryTakeCompletion(key, out var completion))
                {
                    LiveKitFfiProtocolCodec.DecodeOperationSuccess(completion.Bytes, 13, "capture callback");
                    _unknownCaptures.Dequeue();
                }
                if (_unknownCaptures.Count != 0) return false;
            }
            LiveKitFfiCompletionKey? unpublish;
            LiveKitFfiCompletionKey? disconnect;
            lock (_gate) { unpublish = _unknownUnpublish; disconnect = _unknownDisconnect; }
            if (unpublish is { } unpublishKey)
            {
                if (!host.TryTakeCompletion(unpublishKey, out var completion)) return false;
                LiveKitFfiProtocolCodec.DecodeOperationSuccess(completion.Bytes, 10, "unpublish callback");
                lock (_gate) _unknownUnpublish = null;
                await DisconnectAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            if (disconnect is { } disconnectKey)
            {
                if (!host.TryTakeCompletion(disconnectKey, out var completion)) return false;
                LiveKitFfiProtocolCodec.DecodeOperationSuccess(completion.Bytes, 7, "disconnect callback");
                lock (_gate) _unknownDisconnect = null;
                return true;
            }
            return false;
        }
    }

    private sealed class RoomSink(
        LiveKitFfiHost host,
        LiveKitTransportSessionRuntime runtime,
        LiveKitRoomMediaOwner owners,
        AudioFormat format,
        string? requiredParticipantIdentity) : ILiveKitRoomEventSink
    {
        private readonly object _gate = new();
        private string? _selectedIdentity;

        public void ObserveRoomEvent(ReadOnlyMemory<byte> bytes)
        {
            if (LiveKitFfiProtocolCodec.DecodeRoomObservation(bytes.Span) ==
                LiveKitFfiProtocolCodec.RoomObservation.Disconnected)
            {
                runtime.Quarantine("livekit-remote-disconnected");
                return;
            }
            if (!LiveKitFfiProtocolCodec.TryDecodeTrackSubscribed(bytes.Span, out _, out var track) || !track.IsAudio)
                return;
            lock (_gate)
            {
                if (requiredParticipantIdentity is not null &&
                    !string.Equals(requiredParticipantIdentity, track.ParticipantIdentity, StringComparison.Ordinal))
                    return;
                _selectedIdentity ??= track.ParticipantIdentity;
                if (!string.Equals(_selectedIdentity, track.ParticipantIdentity, StringComparison.Ordinal))
                    return;
            }
            try
            {
                var trackOwner = host.Own(LiveKitFfiHandleKind.Track, track.Track);
                var response = host.InvokeSynchronous(LiveKitFfiOperation.NewAudioStream,
                    LiveKitFfiProtocolCodec.NewAudioStream(track.Track, format.SampleRate, format.ChannelCount),
                    CancellationToken.None);
                var streamHandle = LiveKitFfiProtocolCodec.DecodeStreamHandle(response.Bytes);
                var streamOwner = host.Own(LiveKitFfiHandleKind.AudioStream, streamHandle);
                owners.AddDynamic(trackOwner, EmptyRegistration.Instance);
                owners.AddDynamic(streamOwner, host.RegisterAudioStream(streamHandle, runtime));
            }
            catch { runtime.Quarantine("livekit-audio-subscription-failed"); }
        }
    }

    private sealed class DeferredRoomSink : ILiveKitRoomEventSink
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _pending = [];
        private ILiveKitRoomEventSink? _target;

        public void ObserveRoomEvent(ReadOnlyMemory<byte> bytes)
        {
            ILiveKitRoomEventSink? target;
            lock (_gate)
            {
                target = _target;
                if (target is null)
                {
                    if (_pending.Count >= 64)
                        throw new InvalidDataException("LiveKit pre-readiness room event capacity was exceeded.");
                    _pending.Enqueue(bytes.ToArray());
                    return;
                }
            }
            target.ObserveRoomEvent(bytes);
        }

        internal void Attach(ILiveKitRoomEventSink target)
        {
            byte[][] pending;
            lock (_gate)
            {
                if (_target is not null) throw new InvalidOperationException("LiveKit room sink is already attached.");
                _target = target;
                pending = [.. _pending];
                _pending.Clear();
            }
            foreach (var value in pending) target.ObserveRoomEvent(value);
        }
    }

    private sealed class EmptyRegistration : IDisposable
    {
        internal static EmptyRegistration Instance { get; } = new();
        public void Dispose() { }
    }
}

internal sealed class LiveKitManagedAudioSession : IManagedAudioSessionV1
{
    private readonly LiveKitTransportSessionRuntime _runtime;
    private readonly IManagedAudioTranscriptSourceV1 _transcriptSource;
    private readonly GatedAudioSource _input;
    private readonly LiveKitAudioOutputSinkAdapter _output;
    private readonly GatedAudioOutputSink _gatedOutput;
    private int _outputEnabled = 1;
    private int _terminal;
    private string? _unknownStopOperation;

    internal LiveKitManagedAudioSession(
        LiveKitTransportSessionRuntime runtime,
        IManagedAudioTranscriptSourceV1 transcriptSource)
    {
        _runtime = runtime;
        _transcriptSource = transcriptSource;
        _input = new GatedAudioSource(runtime.Inbound);
        _output = new LiveKitAudioOutputSinkAdapter(runtime.Outbound);
        _gatedOutput = new GatedAudioOutputSink(_output, () => Volatile.Read(ref _outputEnabled) != 0);
    }

    public string AudioSessionId => _runtime.AudioSessionId;
    public IAudioOutputSink? OutputSink => _gatedOutput;

    public IAsyncEnumerable<ManagedAudioInputObservationV1> ReadInputObservationsAsync(
        CancellationToken cancellationToken = default) =>
        _transcriptSource.RunAsync(_input, cancellationToken);

    public ValueTask SetInputEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _input.Enabled = enabled;
        return ValueTask.CompletedTask;
    }

    public async ValueTask SetOutputEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _outputEnabled, enabled ? 1 : 0);
        if (!enabled) await _runtime.Outbound.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ManagedAudioOutputInterruptionV1> InterruptOutputAsync(
        string operationId, CancellationToken cancellationToken = default)
    {
        return await _gatedOutput.InterruptActiveAsync(cancellationToken).ConfigureAwait(false)
            ? ManagedAudioOutputInterruptionV1.Interrupted
            : ManagedAudioOutputInterruptionV1.AlreadyIdle;
    }

    public async ValueTask<ManagedAudioSessionStopResultV1> StopAsync(
        AudioSessionStopReason reason, CancellationToken cancellationToken = default)
    {
        var stopped = _unknownStopOperation is { } unknownOperation
            ? await _runtime.ReconcileStopAsync(unknownOperation, cancellationToken).ConfigureAwait(false)
            : await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        if (stopped.Disposition == LiveKitSessionStopDisposition.OutcomeUnknown)
        {
            _unknownStopOperation = stopped.OperationId
                ?? throw new InvalidDataException("LiveKit unknown stop omitted its operation identity.");
            return new ManagedAudioSessionStopResultV1.OutcomeUnknown(_unknownStopOperation);
        }
        _unknownStopOperation = null;
        Interlocked.Exchange(ref _terminal, 1);
        return stopped.Disposition == LiveKitSessionStopDisposition.AlreadyStopped
            ? new ManagedAudioSessionStopResultV1.AlreadyStopped()
            : new ManagedAudioSessionStopResultV1.Stopped();
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _terminal) == 0)
            throw new InvalidOperationException("LiveKit session must stop before disposal.");
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class GatedAudioSource(IAudioSource inner) : IAudioSource
    {
        private int _enabled = 1;
        internal bool Enabled { get => Volatile.Read(ref _enabled) != 0; set => Volatile.Write(ref _enabled, value ? 1 : 0); }
        public AudioFormat Format => inner.Format;
        public bool CanChangeFormat => inner.CanChangeFormat;
        public AudioSourceState State => inner.State;
        public bool TryRead(out AudioFrame frame)
        {
            while (inner.TryRead(out frame)) if (Enabled) return true;
            frame = default;
            return false;
        }
        public async ValueTask<AudioReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var result = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!result.HasFrame || Enabled) return result;
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class GatedAudioOutputSink(
        IAudioOutputSink inner,
        Func<bool> isEnabled) : IAudioOutputSink
    {
        private readonly ConcurrentDictionary<OutputFlowId, byte> _active = [];

        public async ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isEnabled())
                return new OutputSinkStartResult
                {
                    OutputFlowId = stream.OutputFlowId,
                    ResponseId = stream.ResponseId,
                    SegmentId = stream.SegmentId,
                    SegmentIndex = stream.SegmentIndex,
                    Disposition = OutputSinkStartDisposition.Rejected
                };
            var result = await inner.StartAsync(stream, cancellationToken).ConfigureAwait(false);
            if (result.Disposition == OutputSinkStartDisposition.Accepted)
                _active.TryAdd(stream.OutputFlowId, 0);
            return result;
        }

        public ValueTask WriteAsync(
            OutputAudioChunk chunk,
            CancellationToken cancellationToken = default)
        {
            if (!isEnabled())
                return ValueTask.FromException(new InvalidOperationException("LiveKit session output is disabled."));

            // Interruption removes the flow before clearing native playout. Producers
            // can still race us with already-generated chunks; those belong to the old
            // response and must never enter the fresh session-owned write fence.
            return _active.ContainsKey(chunk.OutputFlowId)
                ? inner.WriteAsync(chunk, cancellationToken)
                : ValueTask.CompletedTask;
        }

        public async ValueTask CompleteAsync(
            OutputAudioStreamCompletion completion,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (_active.ContainsKey(completion.OutputFlowId))
                    await inner.CompleteAsync(completion, cancellationToken).ConfigureAwait(false);
            }
            finally { _active.TryRemove(completion.OutputFlowId, out _); }
        }

        internal async ValueTask<bool> InterruptActiveAsync(CancellationToken cancellationToken)
        {
            var flows = _active.Keys.ToArray();
            if (flows.Length == 0) return false;
            foreach (var flow in flows)
            {
                // Fence admission first. Otherwise a producer can submit another stale
                // chunk while the native clear is waiting for the current capture.
                _active.TryRemove(flow, out _);
                await inner.InterruptAsync(flow, cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        public IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default) =>
            inner.ReadPlaybackEventsAsync(outputFlowId, cancellationToken);

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default) =>
            inner.InterruptAsync(outputFlowId, cancellationToken);

        public ValueTask FlushAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default) =>
            inner.FlushAsync(outputFlowId, cancellationToken);
    }
}

internal static class LiveKitParticipantToken
{
    internal static char[] Resolve(
        char[] credential,
        LiveKitTransportProviderConfig options,
        LiveKitAudioSessionBinding binding,
        DateTimeOffset now) => credential.AsSpan().StartsWith("LK1\n", StringComparison.Ordinal)
            ? Mint(credential, options, binding, now)
            : ValidateSupplied(credential, binding, now);

    private static char[] Mint(
        ReadOnlySpan<char> credential,
        LiveKitTransportProviderConfig options,
        LiveKitAudioSessionBinding binding,
        DateTimeOffset now)
    {
        var body = credential[4..];
        var separator = body.IndexOf('\n');
        if (separator is <= 0 || separator == body.Length - 1 || body[(separator + 1)..].Contains('\n'))
            throw new InvalidDataException("LiveKit managed credential envelope is invalid.");
        var apiKey = body[..separator].ToString();
        var apiSecret = body[(separator + 1)..].ToString();
        var expires = now.AddSeconds(options.ParticipantTokenTtlSeconds);
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payloadBuffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(payloadBuffer))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", apiKey);
            writer.WriteString("sub", binding.ParticipantIdentity);
            writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", expires.ToUnixTimeSeconds());
            writer.WriteStartObject("video");
            writer.WriteBoolean("roomJoin", true);
            writer.WriteString("room", binding.RoomName);
            writer.WriteBoolean("canPublish", true);
            writer.WriteBoolean("canSubscribe", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        var payload = Base64Url(payloadBuffer.WrittenSpan);
        var signing = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var secret = Encoding.UTF8.GetBytes(apiSecret);
        var signature = HMACSHA256.HashData(secret, signing);
        try { return $"{header}.{payload}.{Base64Url(signature)}".ToCharArray(); }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(signature);
            CryptographicOperations.ZeroMemory(signing);
        }
    }

    private static char[] ValidateSupplied(
        ReadOnlySpan<char> token,
        LiveKitAudioSessionBinding binding,
        DateTimeOffset now)
    {
        var parts = token.ToString().Split('.');
        if (parts.Length != 3) throw new InvalidDataException("LiveKit participant JWT is malformed.");
        var bytes = DecodeBase64Url(parts[1]);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds) ||
                DateTimeOffset.FromUnixTimeSeconds(seconds) <= now)
                throw new InvalidDataException("LiveKit participant JWT is expired or lacks expiry.");
            if (root.TryGetProperty("sub", out var subject) && subject.GetString() is { } identity &&
                !string.Equals(identity, binding.ParticipantIdentity, StringComparison.Ordinal))
                throw new InvalidDataException("LiveKit participant JWT identity does not match the binding.");
            return token.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + ((4 - text.Length % 4) % 4), '=');
        return Convert.FromBase64String(text);
    }
}
