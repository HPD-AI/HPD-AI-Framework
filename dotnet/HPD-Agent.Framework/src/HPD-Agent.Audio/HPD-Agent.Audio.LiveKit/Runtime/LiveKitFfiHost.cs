using System.Runtime.InteropServices;
using HPD.Agent.Audio.LiveKit.Generated;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.LiveKit;

internal readonly record struct LiveKitFfiNativeResponse(ulong Handle, byte[] Bytes);
internal readonly record struct LiveKitFfiIssuedResponse(
    LiveKitFfiResponseCase ResponseCase,
    ulong AsyncId,
    byte[] Bytes);
internal readonly record struct LiveKitFfiDecodedEvent(
    LiveKitFfiEventCase EventCase,
    ulong AsyncId,
    byte[] Bytes,
    ulong OwnerHandle = 0,
    LiveKitFfiInboundAudioFrame? AudioFrame = null,
    bool AudioStreamEnded = false);
internal readonly record struct LiveKitFfiIssueOutcome(
    LiveKitFfiDecodedEvent? Completion,
    LiveKitFfiCompletionKey? Unknown,
    bool CallerCancellation);
internal readonly record struct LiveKitFfiInboundAudioFrame(
    ulong Handle,
    byte[] Pcm,
    int Channels,
    int SampleRate,
    int SamplesPerChannel);

internal interface ILiveKitFfiWireDecoder
{
    LiveKitFfiIssuedResponse DecodeResponse(ReadOnlySpan<byte> bytes);
    LiveKitFfiDecodedEvent DecodeEvent(ReadOnlySpan<byte> bytes);
}

internal interface ILiveKitRoomEventSink
{
    void ObserveRoomEvent(ReadOnlyMemory<byte> bytes);
}

internal unsafe interface ILiveKitFfiNativeApi
{
    void Initialize(delegate* unmanaged[Cdecl]<nint, nuint, void> callback);
    LiveKitFfiNativeResponse Request(ReadOnlySpan<byte> request);
    bool DropHandle(ulong handle);
    void Dispose();
}

internal sealed unsafe class LiveKitGeneratedNativeApi : ILiveKitFfiNativeApi
{
    public void Initialize(delegate* unmanaged[Cdecl]<nint, nuint, void> callback)
    {
        var sdk = Marshal.StringToCoTaskMemUTF8("hpd-agent-audio-livekit");
        var version = Marshal.StringToCoTaskMemUTF8("1.0.0");
        try { LiveKitFfiNative.Initialize(callback, false, sdk, version); }
        finally
        {
            Marshal.FreeCoTaskMem(version);
            Marshal.FreeCoTaskMem(sdk);
        }
    }

    public LiveKitFfiNativeResponse Request(ReadOnlySpan<byte> request)
    {
        fixed (byte* requestPointer = request)
        {
            var handle = LiveKitFfiNative.Request(
                requestPointer,
                checked((nuint)request.Length),
                out var responsePointer,
                out var responseLength);
            if (handle == 0 || responseLength > int.MaxValue)
                throw new InvalidDataException("LiveKit returned an invalid response buffer.");
            var bytes = new byte[checked((int)responseLength)];
            Marshal.Copy(responsePointer, bytes, 0, bytes.Length);
            return new(handle, bytes);
        }
    }

    public bool DropHandle(ulong handle) => LiveKitFfiNative.DropHandle(handle);
    public void Dispose() => LiveKitFfiNative.Dispose();
}

internal sealed class LiveKitFfiHost : IAsyncDisposable
{
    internal const int MaximumCopiedCallbacks = 256;
    internal const int MaximumIssuedOperations = 256;
    private static readonly object ProcessGate = new();
    private static LiveKitFfiHost? s_processHost;
    private static LiveKitFfiHost? s_callbackTarget;

    private readonly object _gate = new();
    private readonly ILiveKitFfiNativeApi _native;
    private readonly ILiveKitFfiWireDecoder _decoder;
    private readonly LiveKitIssuedOperationRegistry _issued = new();
    private readonly Dictionary<LiveKitFfiCompletionKey, TaskCompletionSource<LiveKitFfiDecodedEvent>> _waiters = [];
    private readonly Dictionary<LiveKitFfiCompletionKey, LiveKitFfiDecodedEvent> _early = [];
    private readonly HashSet<LiveKitFfiCompletionKey> _outstanding = [];
    private readonly Dictionary<ulong, LiveKitTransportSessionRuntime> _audioStreams = [];
    private readonly Dictionary<ulong, long> _audioSequences = [];
    private readonly Dictionary<ulong, ILiveKitRoomEventSink> _rooms = [];
    private readonly Queue<byte[]> _callbacks = [];
    private byte[]? _overflowCleanup;
    private readonly SemaphoreSlim _callbackSignal = new(0);
    private readonly CancellationTokenSource _dispatcherStop = new();
    private readonly Task _dispatcher;
    private string? _quarantineCode;
    private int _callbackAdmissions;
    private int _ownedHandles;
    private bool _disposed;

    private unsafe LiveKitFfiHost(ILiveKitFfiNativeApi native, ILiveKitFfiWireDecoder decoder, bool processGlobal)
    {
        _native = native;
        _decoder = decoder;
        // The native ABI exposes one process callback. Isolated hosts are test-only
        // and therefore use the same slot under a non-parallel test collection.
        s_callbackTarget = this;
        native.Initialize(&NativeCallback);
        _dispatcher = Task.Run(DispatchCallbacksAsync);
    }

    internal static LiveKitFfiHost ProcessGlobal(ILiveKitFfiWireDecoder decoder)
    {
        lock (ProcessGate)
            return s_processHost ??= new(new LiveKitGeneratedNativeApi(), decoder, true);
    }

    internal static LiveKitFfiHost CreateIsolated(ILiveKitFfiNativeApi native, ILiveKitFfiWireDecoder decoder) =>
        new(native, decoder, false);

    internal bool IsQuarantined { get { lock (_gate) return _quarantineCode is not null; } }
    internal string? QuarantineCode { get { lock (_gate) return _quarantineCode; } }
    internal int CallbackAdmissions => Volatile.Read(ref _callbackAdmissions);
    internal int OutstandingOwnedHandles => Volatile.Read(ref _ownedHandles);

    internal IDisposable RegisterAudioStream(ulong streamHandle, LiveKitTransportSessionRuntime session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (streamHandle == 0) throw new ArgumentOutOfRangeException(nameof(streamHandle));
        lock (_gate)
        {
            ThrowIfUnavailableCore();
            if (!_audioStreams.TryAdd(streamHandle, session))
                throw new InvalidOperationException($"LiveKit audio stream {streamHandle} is already registered.");
            _audioSequences.Add(streamHandle, 0);
            session.SelectInboundStream(streamHandle);
        }
        return new AudioStreamRegistration(this, streamHandle, session);
    }

    internal IDisposable RegisterRoom(ulong roomHandle, ILiveKitRoomEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (roomHandle == 0) throw new ArgumentOutOfRangeException(nameof(roomHandle));
        lock (_gate)
        {
            ThrowIfUnavailableCore();
            if (!_rooms.TryAdd(roomHandle, sink))
                throw new InvalidOperationException($"LiveKit room {roomHandle} is already registered.");
        }
        return new RoomRegistration(this, roomHandle, sink);
    }

    internal async ValueTask<LiveKitFfiDecodedEvent?> IssueAsync(
        LiveKitFfiOperation operation,
        ReadOnlyMemory<byte> request,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        var outcome = await IssueTrackedAsync(operation, request, deadline, cancellationToken).ConfigureAwait(false);
        if (outcome.CallerCancellation) throw new OperationCanceledException(cancellationToken);
        return outcome.Completion;
    }

    internal async ValueTask<LiveKitFfiIssueOutcome> IssueTrackedAsync(
        LiveKitFfiOperation operation,
        ReadOnlyMemory<byte> request,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        LiveKitFfiNativeResponse nativeResponse;
        try
        {
            nativeResponse = _native.Request(request.Span);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Quarantine("ffi-request-failure");
            throw new InvalidDataException("LiveKit request failed after native admission.", error);
        }

        try
        {
            var response = _decoder.DecodeResponse(nativeResponse.Bytes);
            if (!LiveKitFfiGeneratedProtocol.TryGetOperation(response.ResponseCase, out var actual) || actual != operation)
            {
                Quarantine("ffi-response-family-mismatch");
                throw new InvalidDataException("LiveKit response did not match the issued operation.");
            }
            if (!LiveKitFfiGeneratedProtocol.TryGetIssuedCompletion(response.ResponseCase, response.AsyncId, out var key))
                return new(null, null, false);

            TaskCompletionSource<LiveKitFfiDecodedEvent> waiter;
            lock (_gate)
            {
                ThrowIfUnavailableCore();
                if (_waiters.Count >= MaximumIssuedOperations)
                {
                    QuarantineCore("ffi-issued-operation-overflow");
                    throw new InvalidDataException("LiveKit issued-operation capacity was exceeded.");
                }
                _issued.Register(key);
                _outstanding.Add(key);
                waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(key, waiter);
                if (_early.Remove(key, out var early))
                {
                    _outstanding.Remove(key);
                    waiter.TrySetResult(early);
                }
            }

            using var deadlineSource = new CancellationTokenSource(deadline);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineSource.Token);
            try
            {
                var completion = await waiter.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                lock (_gate) _waiters.Remove(key);
                return new(completion, null, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_gate) { _issued.Detach(key); _waiters.Remove(key); }
                return new(null, key, true);
            }
            catch (OperationCanceledException) when (deadlineSource.IsCancellationRequested)
            {
                lock (_gate) { _issued.MarkOutcomeUnknown(key); _waiters.Remove(key); }
                return new(null, key, false);
            }
        }
        finally
        {
            DropRequired(nativeResponse.Handle, "response-buffer-release-failed");
        }
    }

    internal bool TryTakeCompletion(LiveKitFfiCompletionKey key, out LiveKitFfiDecodedEvent completion)
    {
        lock (_gate) return _early.Remove(key, out completion);
    }

    internal LiveKitFfiIssuedResponse InvokeSynchronous(
        LiveKitFfiOperation operation,
        ReadOnlySpan<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        LiveKitFfiNativeResponse nativeResponse;
        try { nativeResponse = _native.Request(request); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Quarantine("ffi-request-failure");
            throw new InvalidDataException("LiveKit request failed after native admission.", error);
        }
        try
        {
            var response = _decoder.DecodeResponse(nativeResponse.Bytes);
            if (!LiveKitFfiGeneratedProtocol.TryGetOperation(response.ResponseCase, out var actual) || actual != operation)
            {
                Quarantine("ffi-response-family-mismatch");
                throw new InvalidDataException("LiveKit response did not match the synchronous operation.");
            }
            if (response.AsyncId != 0)
                throw new InvalidDataException("LiveKit synchronous response unexpectedly carried an async ID.");
            return response;
        }
        finally { DropRequired(nativeResponse.Handle, "response-buffer-release-failed"); }
    }

    internal LiveKitIssuedOperationSnapshot Reconcile(LiveKitFfiCompletionKey key) => _issued.Reconcile(key);

    internal LiveKitNativeHandleOwner Own(LiveKitFfiHandleKind kind, ulong value)
    {
        if (value == 0) throw new InvalidDataException("LiveKit returned a null native handle.");
        Interlocked.Increment(ref _ownedHandles);
        return new(this, kind, value);
    }

    internal void ReceiveCopiedCallback(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024)
        {
            Quarantine("ffi-callback-size-invalid");
            return;
        }
        var owned = bytes.ToArray();
        lock (_gate)
        {
            if (_callbacks.Count >= MaximumCopiedCallbacks)
            {
                QuarantineCore("ffi-callback-overflow");
                if (_overflowCleanup is null)
                {
                    _overflowCleanup = owned;
                    _callbackSignal.Release();
                }
                return;
            }
            _callbacks.Enqueue(owned);
            Volatile.Write(ref _callbackAdmissions, _callbacks.Count);
        }
        _callbackSignal.Release();
    }

    private async Task DispatchCallbacksAsync()
    {
        try
        {
            while (true)
            {
                await _callbackSignal.WaitAsync(_dispatcherStop.Token).ConfigureAwait(false);
                byte[] owned;
                lock (_gate)
                {
                    if (_overflowCleanup is not null)
                    {
                        owned = _overflowCleanup;
                        _overflowCleanup = null;
                    }
                    else
                    {
                        if (_callbacks.Count == 0) continue;
                        owned = _callbacks.Dequeue();
                    }
                    Volatile.Write(ref _callbackAdmissions, _callbacks.Count);
                }
                DispatchOne(owned);
            }
        }
        catch (OperationCanceledException) when (_dispatcherStop.IsCancellationRequested)
        {
        }
    }

    private void DispatchOne(byte[] owned)
    {
        try
        {
            var decoded = _decoder.DecodeEvent(owned);
            if (decoded.EventCase == LiveKitFfiEventCase.Panic)
            {
                Quarantine("native-panic");
                return;
            }
            if (decoded.EventCase is LiveKitFfiEventCase.RoomEvent or LiveKitFfiEventCase.AudioStreamEvent)
            {
                LiveKitFfiGeneratedProtocol.RouteEvent(decoded.EventCase, decoded.AsyncId, _issued);
                if (decoded.EventCase == LiveKitFfiEventCase.RoomEvent)
                    DispatchRoomEvent(decoded.OwnerHandle, decoded.Bytes);
                else if (decoded.AudioFrame is { } frame)
                    DispatchAudioFrame(decoded.OwnerHandle, frame);
                else if (decoded.AudioStreamEnded)
                    DispatchAudioEnd(decoded.OwnerHandle);
                return;
            }
            var key = CompletionKey(decoded);
            lock (_gate)
            {
                LiveKitFfiGeneratedProtocol.RouteEvent(decoded.EventCase, decoded.AsyncId, _issued);
                _outstanding.Remove(key);
                if (_waiters.TryGetValue(key, out var waiter)) waiter.TrySetResult(decoded);
                else if (!_early.TryAdd(key, decoded)) { }
            }
        }
        catch
        {
            Quarantine("ffi-callback-corrupt");
        }
    }

    private void DispatchRoomEvent(ulong roomHandle, byte[] bytes)
    {
        ILiveKitRoomEventSink sink;
        lock (_gate)
        {
            if (!_rooms.TryGetValue(roomHandle, out sink!))
            {
                QuarantineCore("ffi-room-event-unowned");
                return;
            }
        }
        try { sink.ObserveRoomEvent(bytes); }
        catch { Quarantine("ffi-room-delivery-failed"); throw; }
    }

    private void DispatchAudioFrame(ulong streamHandle, LiveKitFfiInboundAudioFrame frame)
    {
        try
        {
            LiveKitTransportSessionRuntime session;
            long sequence;
            lock (_gate)
            {
                if (!_audioStreams.TryGetValue(streamHandle, out session!))
                {
                    QuarantineCore("ffi-audio-stream-unowned");
                    return;
                }
                sequence = ++_audioSequences[streamHandle];
            }
            session.AdmitInbound(streamHandle, frame.Pcm, frame.Channels, frame.SampleRate, frame.SamplesPerChannel, sequence);
        }
        catch (AudioSourceException)
        {
            // A bounded session consumer failure is terminal for that session, but
            // it is not evidence that the process-global native host is corrupt.
        }
        catch
        {
            Quarantine("ffi-audio-delivery-failed");
            throw;
        }
        finally
        {
            DropRequired(frame.Handle, "ffi-audio-frame-release-failed");
        }
    }

    private void DispatchAudioEnd(ulong streamHandle)
    {
        LiveKitTransportSessionRuntime session;
        lock (_gate)
        {
            if (!_audioStreams.TryGetValue(streamHandle, out session!))
            {
                QuarantineCore("ffi-audio-stream-unowned");
                return;
            }
        }
        session.CompleteInbound(streamHandle);
    }

    private void UnregisterAudioStream(ulong streamHandle, LiveKitTransportSessionRuntime session)
    {
        lock (_gate)
        {
            if (_audioStreams.TryGetValue(streamHandle, out var current) && ReferenceEquals(current, session))
            {
                _audioStreams.Remove(streamHandle);
                _audioSequences.Remove(streamHandle);
            }
        }
    }

    private void UnregisterRoom(ulong roomHandle, ILiveKitRoomEventSink sink)
    {
        lock (_gate)
            if (_rooms.TryGetValue(roomHandle, out var current) && ReferenceEquals(current, sink))
                _rooms.Remove(roomHandle);
    }

    internal void DropRequired(ulong handle, string safeCode)
    {
        if (!_native.DropHandle(handle))
        {
            Quarantine(safeCode);
            throw new InvalidDataException($"LiveKit native handle {handle} could not be released.");
        }
    }

    internal void DropOwned(ulong handle, string safeCode)
    {
        DropRequired(handle, safeCode);
        Interlocked.Decrement(ref _ownedHandles);
    }

    internal void Quarantine(string safeCode) { lock (_gate) QuarantineCore(safeCode); }
    private void QuarantineCore(string safeCode) => _quarantineCode ??= safeCode;
    private void ThrowIfUnavailable() { lock (_gate) ThrowIfUnavailableCore(); }
    private void ThrowIfUnavailableCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_quarantineCode is { } code) throw new InvalidDataException($"LiveKit FFI host is quarantined: {code}.");
    }

    private static LiveKitFfiCompletionKey CompletionKey(LiveKitFfiDecodedEvent value)
    {
        var operation = value.EventCase switch
        {
            LiveKitFfiEventCase.Connect => LiveKitFfiOperation.Connect,
            LiveKitFfiEventCase.PublishTrack => LiveKitFfiOperation.PublishTrack,
            LiveKitFfiEventCase.CaptureAudioFrame => LiveKitFfiOperation.CaptureAudioFrame,
            LiveKitFfiEventCase.UnpublishTrack => LiveKitFfiOperation.UnpublishTrack,
            LiveKitFfiEventCase.Disconnect => LiveKitFfiOperation.Disconnect,
            _ => throw new InvalidDataException("LiveKit callback was not an issued completion.")
        };
        return new(operation, value.AsyncId);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void NativeCallback(nint data, nuint length)
    {
        var target = s_callbackTarget;
        if (target is null) return;
        if (length > int.MaxValue) { target.Quarantine("ffi-callback-size-invalid"); return; }
        try { target.ReceiveCopiedCallback(new ReadOnlySpan<byte>((void*)data, checked((int)length))); }
        catch { target.Quarantine("ffi-callback-corrupt"); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_waiters.Count != 0) throw new InvalidOperationException("Cannot dispose LiveKit FFI host with issued operations.");
            if (_outstanding.Count != 0) throw new InvalidOperationException("Cannot dispose LiveKit FFI host with unresolved issued operations.");
            if (_audioStreams.Count != 0) throw new InvalidOperationException("Cannot dispose LiveKit FFI host with registered audio streams.");
            if (_rooms.Count != 0) throw new InvalidOperationException("Cannot dispose LiveKit FFI host with registered rooms.");
            if (_ownedHandles != 0 && _quarantineCode is null)
                throw new InvalidOperationException($"Cannot dispose LiveKit FFI host with {_ownedHandles} owned native handles.");
            _disposed = true;
        }
        _native.Dispose();
        if (ReferenceEquals(s_callbackTarget, this)) s_callbackTarget = null;
        _dispatcherStop.Cancel();
        await _dispatcher.ConfigureAwait(false);
        _dispatcherStop.Dispose();
        _callbackSignal.Dispose();
    }

    private sealed class AudioStreamRegistration(
        LiveKitFfiHost host,
        ulong streamHandle,
        LiveKitTransportSessionRuntime session) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                host.UnregisterAudioStream(streamHandle, session);
        }
    }

    private sealed class RoomRegistration(LiveKitFfiHost host, ulong roomHandle, ILiveKitRoomEventSink sink) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                host.UnregisterRoom(roomHandle, sink);
        }
    }
}

