using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using System.Threading.Channels;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public sealed record DebugProtocolClientOptions
{
    public int MaxPendingRequests { get; init; } = 128;
    public int MaxTombstones { get; init; } = 256;
    public int MaxConcurrentReverseRequests { get; init; } = 16;
    public int MaxReverseRequestsPerMinute { get; init; } = 60;
    public int MaxActiveProgressEntries { get; init; } = 128;
    public int MaxQueuedEvents { get; init; } = 256;
    public TimeSpan ProgressOrphanLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ReverseRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public DebugProtocolFramingLimits Framing { get; init; } = new();
    public bool RequireInitializeFirst { get; init; } = true;
    public IDebugProtocolTraceSink? HostTraceSink { get; init; }
}

public sealed record DebugProtocolEventMessage(int Sequence, string Event, JsonElement? Body);
public sealed record DebugProtocolReverseRequest(int Sequence, string Command, JsonElement? Arguments);
public sealed record DebugProtocolFault(string ReasonCode);
public sealed record DebugAdapterDiagnosticSnapshot(
    string StandardError,
    long DroppedChunks,
    long DroppedBytes,
    DebugTransportExit? Exit);

public sealed record DebugProtocolClientHealth(
    int QueuedEvents,
    long ProcessedEvents,
    long EventHandlerFailures,
    string? LastFailedEvent,
    string? LastHandlerFailureType,
    long TraceRecordsDropped);

public sealed record DebugAdapterError
{
    public required string Command { get; init; }
    public string? ResponseMessage { get; init; }
    public int? Id { get; init; }
    public string? Format { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public bool ShowUser { get; init; }
    public string? ApprovedUrl { get; init; }
    public string? UrlLabel { get; init; }
}

public sealed class DebugAdapterRequestException(DebugAdapterError error)
    : Exception($"Debug adapter request '{error.Command}' failed.")
{
    public DebugAdapterError Error { get; } = error;
}

public sealed class DebugProtocolException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class DebugProtocolClient : IAsyncDisposable
{
    private readonly IDebugProtocolTransport _transport;
    private readonly DebugProtocolClientOptions _options;
    private readonly DebugProtocolFramer _framer;
    private readonly ConcurrentDictionary<int, IPendingRequest> _pending = new();
    private readonly ConcurrentDictionary<int, TombstoneKind> _tombstones = new();
    private readonly ConcurrentQueue<int> _tombstoneOrder = new();
    private readonly ConcurrentDictionary<string, ProgressState> _progress = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<DebugProtocolReverseRequest, CancellationToken, ValueTask<JsonElement?>>> _reverseHandlers = new(StringComparer.Ordinal);
    private readonly List<Func<DebugProtocolEventMessage, ValueTask>> _eventHandlers = [];
    private readonly object _eventLock = new();
    private readonly List<Action<DebugProtocolFault>> _faultHandlers = [];
    private readonly object _faultLock = new();
    private readonly Channel<QueuedProtocolEvent> _events;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _eventDispatcher;
    private readonly Task _reader;
    private readonly Task _diagnosticReader;
    private readonly Task _exitObserver;
    private readonly object _diagnosticLock = new();
    private readonly MemoryStream _diagnosticBytes = new();
    private long _diagnosticDroppedChunks;
    private long _diagnosticDroppedBytes;
    private DebugTransportExit? _transportExit;
    private int _sequence;
    private int _disposed;
    private int _pendingCount;
    private int _activeReverseRequests;
    private int _initializeState;
    private int _queuedEventCount;
    private long _processedEventCount;
    private long _eventHandlerFailureCount;
    private string? _lastFailedEvent;
    private string? _lastHandlerFailureType;
    private long _traceRecordsDropped;
    private int _faultPublished;
    private readonly object _reverseRateLock = new();
    private readonly Queue<DateTimeOffset> _reverseRequestTimes = new();
    private volatile bool _supportsCancelRequest;

    public DebugProtocolClient(IDebugProtocolTransport transport, DebugProtocolClientOptions? options = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new();
        if (_options.MaxPendingRequests is <= 0 or > 4096 || _options.MaxTombstones is <= 0 or > 8192 ||
            _options.MaxConcurrentReverseRequests is <= 0 or > 256 || _options.MaxReverseRequestsPerMinute is <= 0 or > 4096 ||
            _options.MaxActiveProgressEntries is <= 0 or > 4096 || _options.MaxQueuedEvents is <= 0 or > 8192 ||
            _options.RequestTimeout <= TimeSpan.Zero ||
            _options.WriteTimeout <= TimeSpan.Zero || _options.ReverseRequestTimeout <= TimeSpan.Zero || _options.ProgressOrphanLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        _framer = new(_options.Framing);
        _events = Channel.CreateBounded<QueuedProtocolEvent>(new BoundedChannelOptions(_options.MaxQueuedEvents)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _eventDispatcher = DispatchEventsAsync();
        _diagnosticReader = ReadDiagnosticsAsync();
        _exitObserver = ObserveExitAsync();
        _reader = ReaderLoopAsync();
    }

    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && _transport.IsAlive && !_reader.IsCompleted;
    public int PendingRequestCount => _pending.Count;
    public IReadOnlyCollection<string> ActiveProgressIds => _progress.Keys.ToArray();
    public DebugProtocolClientHealth Health => new(
        Volatile.Read(ref _queuedEventCount),
        Interlocked.Read(ref _processedEventCount),
        Interlocked.Read(ref _eventHandlerFailureCount),
        Volatile.Read(ref _lastFailedEvent),
        Volatile.Read(ref _lastHandlerFailureType),
        Interlocked.Read(ref _traceRecordsDropped));
    public DebugAdapterDiagnosticSnapshot AdapterDiagnostics
    {
        get
        {
            lock (_diagnosticLock)
                return new(
                    Encoding.UTF8.GetString(_diagnosticBytes.ToArray()),
                    _diagnosticDroppedChunks,
                    _diagnosticDroppedBytes,
                    _transportExit);
        }
    }

    public void SetSupportsCancelRequest(bool supported) => _supportsCancelRequest = supported;

    public IDisposable OnEvent(Func<DebugProtocolEventMessage, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_eventLock) _eventHandlers.Add(handler);
        return new CallbackRegistration(() => { lock (_eventLock) _eventHandlers.Remove(handler); });
    }

    public IDisposable OnFault(Action<DebugProtocolFault> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_faultLock) _faultHandlers.Add(handler);
        return new CallbackRegistration(() => { lock (_faultLock) _faultHandlers.Remove(handler); });
    }

    public IDisposable OnEvent<TBody>(DapEventDescriptor<TBody> descriptor, Func<TBody, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        return OnEvent(message =>
        {
            if (!string.Equals(message.Event, descriptor.Event, StringComparison.Ordinal)) return ValueTask.CompletedTask;
            var body = message.Body is { } value
                ? value.Deserialize(descriptor.BodyTypeInfo)
                : JsonSerializer.Deserialize("{}", descriptor.BodyTypeInfo);
            return handler(body ?? throw new JsonException($"DAP event '{descriptor.Event}' has no body."));
        });
    }

    public IDisposable RegisterReverseRequestHandler(
        string command,
        Func<DebugProtocolReverseRequest, CancellationToken, ValueTask<JsonElement?>> handler)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("A command is required.", nameof(command));
        ArgumentNullException.ThrowIfNull(handler);
        if (!_reverseHandlers.TryAdd(command, handler))
            throw new InvalidOperationException($"A reverse-request handler for '{command}' is already registered.");
        return new CallbackRegistration(() => _reverseHandlers.TryRemove(command, out _));
    }

    public IDisposable RegisterReverseRequestHandler<TArguments, TBody>(
        DapRequestDescriptor<TArguments, TBody> descriptor,
        Func<TArguments, CancellationToken, ValueTask<TBody>> handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (descriptor.Direction != DapRequestDirection.AdapterToClient)
            throw new InvalidOperationException($"'{descriptor.Command}' is not an adapter-to-client request.");
        return RegisterReverseRequestHandler(descriptor.Command, async (request, cancellationToken) =>
        {
            var arguments = request.Arguments is { } value
                ? value.Deserialize(descriptor.ArgumentsTypeInfo)
                : JsonSerializer.Deserialize("{}", descriptor.ArgumentsTypeInfo);
            var result = await handler(arguments ?? throw new JsonException(
                $"DAP reverse request '{descriptor.Command}' has invalid arguments."), cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(result, descriptor.BodyTypeInfo);
        });
    }

    public async ValueTask<TBody> SendAsync<TArguments, TBody>(
        DapRequestDescriptor<TArguments, TBody> descriptor,
        TArguments arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (descriptor.Direction != DapRequestDirection.ClientToAdapter)
            throw new InvalidOperationException($"'{descriptor.Command}' is an adapter-to-client request.");
        var isInitialize = string.Equals(descriptor.Command, DebugProtocolDescriptors.InitializeRequest.Command, StringComparison.Ordinal);
        if (_options.RequireInitializeFirst)
        {
            if (isInitialize && Interlocked.CompareExchange(ref _initializeState, 1, 0) != 0)
                throw new InvalidOperationException("DAP initialize may be sent exactly once.");
            if (!isInitialize && descriptor.Command != DebugProtocolDescriptors.CancelRequest.Command && Volatile.Read(ref _initializeState) != 2)
                throw new InvalidOperationException("DAP initialize must complete before other requests are sent.");
        }
        if (!TryReservePending())
            throw new DebugProtocolException("PENDING_REQUEST_LIMIT", "The DAP pending-request limit was reached.");
        var sequence = NextSequence();
        var pending = new PendingRequest<TBody>(sequence, descriptor.Command, descriptor.BodyTypeInfo);
        if (!_pending.TryAdd(sequence, pending))
        {
            Interlocked.Decrement(ref _pendingCount);
            throw new DebugProtocolException("SEQUENCE_COLLISION", "A DAP request sequence collision occurred.");
        }

        using var timeoutCts = new CancellationTokenSource(timeout ?? _options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token, _lifetime.Token);
        using var registration = linked.Token.Register(() => CancelPending(sequence, pending, cancellationToken.IsCancellationRequested));
        try
        {
            await WriteRequestAsync(sequence, descriptor, arguments, linked.Token).ConfigureAwait(false);
            var result = await pending.Task.ConfigureAwait(false);
            if (isInitialize)
            {
                Volatile.Write(ref _initializeState, 2);
                if (result is Capabilities capabilities)
                    SetSupportsCancelRequest(capabilities.SupportsCancelRequest == true);
            }
            return result;
        }
        catch
        {
            if (isInitialize) Volatile.Write(ref _initializeState, -1);
            if (_pending.TryRemove(sequence, out _))
            {
                Interlocked.Decrement(ref _pendingCount);
                AddTombstone(sequence, TombstoneKind.Cancelled);
            }
            throw;
        }
    }

    public ValueTask<Capabilities> InitializeAsync(
        InitializeRequestArguments arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
        => SendAsync(DebugProtocolDescriptors.InitializeRequest, arguments, cancellationToken, timeout ?? TimeSpan.FromSeconds(30));

    public async ValueTask<bool> CancelProgressAsync(string progressId, CancellationToken cancellationToken = default)
    {
        if (!_progress.TryGetValue(progressId, out var progress) || !progress.Cancellable)
            return false;
        await SendCancelAsync(progress.RequestId, progressId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        SettleAll(new ObjectDisposedException(nameof(DebugProtocolClient)));
        _lifetime.Cancel();
        try { await _transport.StopAsync(new(Reason: "PROTOCOL_CLIENT_DISPOSED")).ConfigureAwait(false); } catch { }
        try { await _reader.ConfigureAwait(false); } catch { }
        try { await _diagnosticReader.ConfigureAwait(false); } catch { }
        try { await _exitObserver.ConfigureAwait(false); } catch { }
        _events.Writer.TryComplete();
        try { await _eventDispatcher.ConfigureAwait(false); } catch { }
        await _transport.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task ReadDiagnosticsAsync()
    {
        try
        {
            await foreach (var chunk in _transport.ReadDiagnosticsAsync(_lifetime.Token).ConfigureAwait(false))
            {
                lock (_diagnosticLock)
                {
                    _diagnosticDroppedChunks = Math.Max(_diagnosticDroppedChunks, chunk.DroppedChunks);
                    _diagnosticDroppedBytes = Math.Max(_diagnosticDroppedBytes, chunk.DroppedBytes);
                    var remaining = 64 * 1024 - checked((int)_diagnosticBytes.Length);
                    if (remaining > 0)
                    {
                        var bytes = chunk.Bytes.Span[..Math.Min(remaining, chunk.Bytes.Length)];
                        _diagnosticBytes.Write(bytes);
                        if (bytes.Length < chunk.Bytes.Length)
                            _diagnosticDroppedBytes += chunk.Bytes.Length - bytes.Length;
                    }
                    else
                    {
                        _diagnosticDroppedBytes += chunk.Bytes.Length;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task ObserveExitAsync()
    {
        try
        {
            var exit = await _transport.WaitForExitAsync(_lifetime.Token).ConfigureAwait(false);
            lock (_diagnosticLock)
                _transportExit = exit;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task WriteRequestAsync<TArguments, TBody>(
        int sequence,
        DapRequestDescriptor<TArguments, TBody> descriptor,
        TArguments arguments,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", sequence);
            writer.WriteString("type", "request");
            writer.WriteString("command", descriptor.Command);
            writer.WritePropertyName("arguments");
            JsonSerializer.Serialize(writer, arguments, descriptor.ArgumentsTypeInfo);
            writer.WriteEndObject();
        }
        await WriteFrameAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        Trace(DebugProtocolTraceDirection.Outbound, payload.Span);
        var frame = DebugProtocolFramer.Encode(payload.Span, _options.Framing);
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        writeCts.CancelAfter(_options.WriteTimeout);
        await _writeLock.WaitAsync(writeCts.Token).ConfigureAwait(false);
        try
        {
            var write = _transport.WriteProtocolAsync(frame, writeCts.Token).AsTask();
            try
            {
                await write.WaitAsync(writeCts.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!write.IsCompleted)
                    _ = write.ContinueWith(
                        static completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted |
                            TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                throw;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await FaultAsync(new DebugProtocolException("WRITE_TIMEOUT", "The DAP protocol write timed out.")).ConfigureAwait(false);
            throw;
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReaderLoopAsync()
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var read = await _transport.ReadProtocolAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (read == 0)
                    throw new DebugProtocolException("TRANSPORT_EOF", "The DAP transport closed.");
                foreach (var frame in _framer.Append(buffer.AsSpan(0, read)))
                    ProcessFrame(frame);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await FaultAsync(exception).ConfigureAwait(false);
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    private void ProcessFrame(ReadOnlyMemory<byte> frame)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(frame); }
        catch (JsonException) { throw new DebugProtocolException("MALFORMED_JSON", "The framed DAP payload is not valid JSON."); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryRequiredInt(root, "seq", out var sequence) ||
                !TryRequiredString(root, "type", out var type))
                throw new DebugProtocolException("MALFORMED_ENVELOPE", "The DAP message envelope is invalid.");
            Trace(DebugProtocolTraceDirection.Inbound, frame.Span);
            switch (type)
            {
                case "response": ProcessResponse(root); break;
                case "event": ProcessEvent(sequence, root); break;
                case "request": ProcessReverseRequest(sequence, root); break;
                default: throw new DebugProtocolException("UNKNOWN_MESSAGE_TYPE", "The DAP message type is unsupported.");
            }
        }
    }

    private void ProcessResponse(JsonElement root)
    {
        if (!TryRequiredInt(root, "request_seq", out var requestSequence) ||
            !TryRequiredString(root, "command", out var command) ||
            !root.TryGetProperty("success", out var successElement) || successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DebugProtocolException("MALFORMED_RESPONSE", "The DAP response envelope is invalid.");
        if (!_pending.TryRemove(requestSequence, out var pending))
        {
            if (_tombstones.TryGetValue(requestSequence, out var tombstone) && tombstone == TombstoneKind.Cancelled)
                return;
            throw new DebugProtocolException("UNKNOWN_OR_DUPLICATE_RESPONSE", "The DAP response does not identify a live request.");
        }
        Interlocked.Decrement(ref _pendingCount);
        if (!string.Equals(command, pending.Command, StringComparison.Ordinal))
        {
            pending.Fail(new DebugProtocolException("RESPONSE_COMMAND_MISMATCH", "The DAP response command does not match its request."));
            throw new DebugProtocolException("RESPONSE_COMMAND_MISMATCH", "The DAP response command does not match its request.");
        }
        AddTombstone(requestSequence, TombstoneKind.Completed);
        if (!successElement.GetBoolean())
        {
            pending.Fail(new DebugAdapterRequestException(ParseAdapterError(command, root)));
            return;
        }
        pending.Succeed(root.TryGetProperty("body", out var body) ? body : null);
    }

    private void ProcessEvent(int sequence, JsonElement root)
    {
        if (!TryRequiredString(root, "event", out var eventName))
            throw new DebugProtocolException("MALFORMED_EVENT", "The DAP event name is missing.");
        JsonElement? body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.Clone() : null;
        Func<DebugProtocolEventMessage, ValueTask>[] handlers;
        lock (_eventLock) handlers = _eventHandlers.ToArray();
        var message = new DebugProtocolEventMessage(sequence, eventName, body);
        Interlocked.Increment(ref _queuedEventCount);
        if (!_events.Writer.TryWrite(new(message, handlers)))
        {
            Interlocked.Decrement(ref _queuedEventCount);
            throw new DebugProtocolException("EVENT_QUEUE_LIMIT", "The bounded DAP semantic event queue is full.");
        }
    }

    private async Task DispatchEventsAsync()
    {
        try
        {
            await foreach (var queued in _events.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                Interlocked.Decrement(ref _queuedEventCount);
                TrackProgress(queued.Message.Event, queued.Message.Body);
                foreach (var handler in queued.Handlers)
                {
                    try
                    {
                        await handler(queued.Message).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref _eventHandlerFailureCount);
                        Volatile.Write(ref _lastFailedEvent, Bound(queued.Message.Event, 128));
                        Volatile.Write(ref _lastHandlerFailureType, Bound(exception.GetType().FullName, 256));
                    }
                }
                Interlocked.Increment(ref _processedEventCount);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void ProcessReverseRequest(int sequence, JsonElement root)
    {
        if (!TryRequiredString(root, "command", out var command))
            throw new DebugProtocolException("MALFORMED_REVERSE_REQUEST", "The DAP reverse request command is missing.");
        var request = new DebugProtocolReverseRequest(
            sequence,
            command,
            root.TryGetProperty("arguments", out var arguments) ? arguments.Clone() : null);
        if (!TryAcceptReverseRequest())
        {
            _ = Task.Run(() => WriteReverseResponseAsync(request, false, null, "tooManyRequests"));
            return;
        }
        _ = Task.Run(() => HandleReverseRequestAsync(request));
    }

    private async Task HandleReverseRequestAsync(DebugProtocolReverseRequest request)
    {
        try
        {
            if (!_reverseHandlers.TryGetValue(request.Command, out var handler))
            {
                await WriteReverseResponseAsync(request, success: false, body: null, "notSupported").ConfigureAwait(false);
                return;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(_options.ReverseRequestTimeout);
            var body = await handler(request, timeout.Token).ConfigureAwait(false);
            await WriteReverseResponseAsync(request, success: true, body, null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            await WriteReverseResponseAsync(request, success: false, body: null, "timeout").ConfigureAwait(false);
        }
        catch (Exception)
        {
            await WriteReverseResponseAsync(request, success: false, body: null, "handlerFailed").ConfigureAwait(false);
        }
        finally { Interlocked.Decrement(ref _activeReverseRequests); }
    }

    private async Task WriteReverseResponseAsync(DebugProtocolReverseRequest request, bool success, JsonElement? body, string? message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", NextSequence());
            writer.WriteString("type", "response");
            writer.WriteNumber("request_seq", request.Sequence);
            writer.WriteBoolean("success", success);
            writer.WriteString("command", request.Command);
            if (message is not null) writer.WriteString("message", message);
            if (body is { } value) { writer.WritePropertyName("body"); value.WriteTo(writer); }
            writer.WriteEndObject();
        }
        await WriteFrameAsync(buffer.WrittenMemory, _lifetime.Token).ConfigureAwait(false);
    }

    private void CancelPending(int sequence, IPendingRequest pending, bool callerCancelled)
    {
        if (!_pending.TryRemove(sequence, out _)) return;
        Interlocked.Decrement(ref _pendingCount);
        AddTombstone(sequence, TombstoneKind.Cancelled);
        pending.Cancel(callerCancelled);
        if (_supportsCancelRequest && pending.Command != DebugProtocolDescriptors.CancelRequest.Command)
            _ = SendCancelAsync(sequence, null, CancellationToken.None).AsTask();
    }

    private async ValueTask SendCancelAsync(int? requestId, string? progressId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(DebugProtocolDescriptors.CancelRequest, new CancelArguments
            {
                RequestId = requestId,
                ProgressId = progressId
            }, cancellationToken, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch { }
    }

    private void TrackProgress(string eventName, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("progressId", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            return;
        var id = idElement.GetString()!;
        var cutoff = DateTimeOffset.UtcNow - _options.ProgressOrphanLifetime;
        foreach (var entry in _progress)
            if (entry.Value.StartedAt < cutoff) _progress.TryRemove(entry.Key, out _);
        if (eventName == "progressStart")
        {
            int? requestId = value.TryGetProperty("requestId", out var request) && request.TryGetInt32(out var parsed) ? parsed : null;
            var cancellable = value.TryGetProperty("cancellable", out var can) && can.ValueKind == JsonValueKind.True;
            if (_progress.Count >= _options.MaxActiveProgressEntries && !_progress.ContainsKey(id)) return;
            _progress[id] = new(requestId, cancellable, DateTimeOffset.UtcNow);
        }
        else if (eventName == "progressEnd")
            _progress.TryRemove(id, out _);
    }

    private async Task FaultAsync(Exception exception)
    {
        SettleAll(exception);
        _progress.Clear();
        try { await _transport.StopAsync(new(Reason: "PROTOCOL_FAULT")).ConfigureAwait(false); } catch { }
        if (Interlocked.CompareExchange(ref _faultPublished, 1, 0) == 0)
        {
            var fault = new DebugProtocolFault(exception is DebugProtocolException protocol
                ? Bound(protocol.ReasonCode, 128)! : "PROTOCOL_CLIENT_FAULT");
            Action<DebugProtocolFault>[] handlers;
            lock (_faultLock) handlers = _faultHandlers.ToArray();
            foreach (var handler in handlers)
                try { handler(fault); } catch { }
        }
    }

    private void SettleAll(Exception exception)
    {
        foreach (var (sequence, pending) in _pending.ToArray())
            if (_pending.TryRemove(sequence, out _))
            {
                Interlocked.Decrement(ref _pendingCount);
                pending.Fail(exception);
            }
    }

    private bool TryReservePending()
    {
        while (true)
        {
            var current = Volatile.Read(ref _pendingCount);
            if (current >= _options.MaxPendingRequests) return false;
            if (Interlocked.CompareExchange(ref _pendingCount, current + 1, current) == current) return true;
        }
    }

    private bool TryAcceptReverseRequest()
    {
        if (Interlocked.Increment(ref _activeReverseRequests) > _options.MaxConcurrentReverseRequests)
        {
            Interlocked.Decrement(ref _activeReverseRequests);
            return false;
        }
        lock (_reverseRateLock)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
            while (_reverseRequestTimes.TryPeek(out var timestamp) && timestamp < cutoff)
                _reverseRequestTimes.Dequeue();
            if (_reverseRequestTimes.Count >= _options.MaxReverseRequestsPerMinute)
            {
                Interlocked.Decrement(ref _activeReverseRequests);
                return false;
            }
            _reverseRequestTimes.Enqueue(DateTimeOffset.UtcNow);
            return true;
        }
    }

    private void AddTombstone(int sequence, TombstoneKind kind)
    {
        _tombstones[sequence] = kind;
        _tombstoneOrder.Enqueue(sequence);
        while (_tombstoneOrder.Count > _options.MaxTombstones && _tombstoneOrder.TryDequeue(out var expired))
            _tombstones.TryRemove(expired, out _);
    }

    private int NextSequence()
    {
        var sequence = Interlocked.Increment(ref _sequence);
        if (sequence <= 0)
            throw new DebugProtocolException("SEQUENCE_EXHAUSTED", "The DAP sequence space was exhausted.");
        return sequence;
    }

    private static DebugAdapterError ParseAdapterError(string command, JsonElement root)
    {
        string? responseMessage = root.TryGetProperty("message", out var response) && response.ValueKind == JsonValueKind.String
            ? Bound(response.GetString(), 1024) : null;
        if (!root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return new() { Command = command, ResponseMessage = responseMessage };
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (error.TryGetProperty("variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
            foreach (var property in vars.EnumerateObject().Take(32))
                if (property.Value.ValueKind == JsonValueKind.String)
                    variables[Bound(property.Name, 128)!] = Bound(property.Value.GetString(), 1024)!;
        string? url = error.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
            ? ApprovedUrl(urlElement.GetString()) : null;
        return new()
        {
            Command = command,
            ResponseMessage = responseMessage,
            Id = error.TryGetProperty("id", out var id) && id.TryGetInt32(out var parsedId) ? parsedId : null,
            Format = error.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String ? Bound(format.GetString(), 2048) : null,
            Variables = variables,
            ShowUser = error.TryGetProperty("showUser", out var show) && show.ValueKind == JsonValueKind.True,
            ApprovedUrl = url,
            UrlLabel = url is not null && error.TryGetProperty("urlLabel", out var label) && label.ValueKind == JsonValueKind.String ? Bound(label.GetString(), 256) : null
        };
    }

    private static string? ApprovedUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.ToString() : null;
    private static string? Bound(string? value, int limit) => value is null ? null : value[..Math.Min(value.Length, limit)];
    private void Trace(DebugProtocolTraceDirection direction, ReadOnlySpan<byte> payload)
    {
        if (_options.HostTraceSink is null) return;
        try
        {
            if (!_options.HostTraceSink.TryRecord(direction, payload))
                Interlocked.Increment(ref _traceRecordsDropped);
        }
        catch { Interlocked.Increment(ref _traceRecordsDropped); }
    }
    private static bool TryRequiredInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) && property.TryGetInt32(out value);
    }
    private static bool TryRequiredString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()!;
        return !string.IsNullOrWhiteSpace(value);
    }

    private enum TombstoneKind { Cancelled, Completed }
    private sealed record ProgressState(int? RequestId, bool Cancellable, DateTimeOffset StartedAt);
    private sealed record QueuedProtocolEvent(
        DebugProtocolEventMessage Message,
        Func<DebugProtocolEventMessage, ValueTask>[] Handlers);

    private interface IPendingRequest
    {
        string Command { get; }
        void Succeed(JsonElement? body);
        void Fail(Exception exception);
        void Cancel(bool callerCancelled);
    }

    private sealed class PendingRequest<T>(int sequence, string command, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) : IPendingRequest
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Command { get; } = command;
        public Task<T> Task => _completion.Task;
        public void Succeed(JsonElement? body)
        {
            try
            {
                var value = body is { } element
                    ? element.Deserialize(typeInfo)
                    : JsonSerializer.Deserialize("{}", typeInfo);
                _completion.TrySetResult(value ?? throw new JsonException($"DAP response body for sequence {sequence} was null."));
            }
            catch (Exception exception) { _completion.TrySetException(exception); }
        }
        public void Fail(Exception exception) => _completion.TrySetException(exception);
        public void Cancel(bool callerCancelled)
        {
            if (callerCancelled) _completion.TrySetCanceled();
            else _completion.TrySetException(new TimeoutException($"DAP request '{Command}' timed out."));
        }
    }

    private sealed class CallbackRegistration(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
