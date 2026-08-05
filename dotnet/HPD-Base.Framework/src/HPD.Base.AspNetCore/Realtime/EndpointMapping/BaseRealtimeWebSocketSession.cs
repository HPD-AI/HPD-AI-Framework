using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Base;
using HPD.Events;
using Microsoft.Extensions.Logging;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeWebSocketSession
{
    private const string PayloadTooLargeMessageType = "__payloadTooLarge";
    private const string NonTextMessageType = "__nonText";
    private const string InvalidJsonMessageType = "__invalidJson";

    private readonly WebSocket _socket;
    private readonly IBaseRealtimeFeedSource _feeds;
    private readonly JsonSerializerOptions _json;
    private readonly BaseRealtimeOptions _options;
    private readonly BaseRealtimeStats _stats;
    private readonly PrincipalContext _principal;
    private readonly ILogger<BaseRealtimeWebSocketSession> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly BaseRealtimeJoinRateLimiter _joinRateLimiter;
    private readonly Dictionary<string, BaseRealtimeChannelOwner> _channels = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Initializes a new instance.</summary>
    public BaseRealtimeWebSocketSession(
        WebSocket socket,
        IBaseRealtimeFeedSource feeds,
        JsonSerializerOptions json,
        BaseRealtimeOptions options,
        BaseRealtimeStats stats,
        PrincipalContext principal,
        ILogger<BaseRealtimeWebSocketSession> logger,
        TimeProvider timeProvider)
    {
        _socket = socket;
        _feeds = feeds;
        _json = json;
        _options = options;
        _stats = stats;
        _principal = principal;
        _logger = logger;
        _timeProvider = timeProvider;
        _joinRateLimiter = new BaseRealtimeJoinRateLimiter(
            timeProvider,
            options.Limits.MaxJoinsPerSecond);
    }

    /// <summary>Executes the run async operation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        HPDBaseRealtimeAspNetCoreLog.ConnectionOpened(_logger);
        try
        {
            {
                using var connectionActivity = HPDBaseRealtimeAspNetCoreTelemetry.StartConnection();
                await SendAsync(new BaseRealtimeServerMessage
                {
                    Type = BaseRealtimeProtocolTypes.Connected
                }, cancellationToken).ConfigureAwait(false);
                HPDBaseRealtimeAspNetCoreTelemetry.Finish(connectionActivity, "ok");
            }

            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                    break;

                await HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopChannelsAsync().ConfigureAwait(false);
            _sendLock.Dispose();
        }
    }

    private async Task HandleAsync(BaseRealtimeClientMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case BaseRealtimeProtocolTypes.Heartbeat:
                await SendAsync(new BaseRealtimeServerMessage { Type = BaseRealtimeProtocolTypes.Heartbeat, Ref = message.Ref }, cancellationToken).ConfigureAwait(false);
                break;
            case PayloadTooLargeMessageType:
                await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.PayloadTooLarge, "Realtime message payload exceeded the configured limit.", cancellationToken).ConfigureAwait(false);
                break;
            case NonTextMessageType:
                HPDBaseRealtimeAspNetCoreLog.ProtocolMessageUnsupported(
                    _logger, "nonText", BaseRealtimeErrorCodes.ProtocolInvalid);
                await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ProtocolInvalid, "Unsupported realtime protocol message type.", cancellationToken).ConfigureAwait(false);
                break;
            case InvalidJsonMessageType:
                HPDBaseRealtimeAspNetCoreLog.ProtocolMessageUnsupported(
                    _logger, "invalidJson", BaseRealtimeErrorCodes.ProtocolInvalid);
                await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ProtocolInvalid, "Unsupported realtime protocol message type.", cancellationToken).ConfigureAwait(false);
                break;
            case BaseRealtimeProtocolTypes.Join:
                await JoinAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case BaseRealtimeProtocolTypes.Leave:
                await LeaveAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            default:
                HPDBaseRealtimeAspNetCoreLog.ProtocolMessageUnsupported(
                    _logger, "unsupportedType", BaseRealtimeErrorCodes.ProtocolInvalid);
                await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ProtocolInvalid, "Unsupported realtime protocol message type.", cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task JoinAsync(BaseRealtimeClientMessage message, CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRealtimeAspNetCoreTelemetry.StartJoin(ChannelKindValue(message.Config?.Kind));
        if (string.IsNullOrWhiteSpace(message.Channel) || message.Config is null)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedProtocol(_logger, BaseRealtimeErrorCodes.ProtocolInvalid);
            await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ProtocolInvalid, "Join messages require channel and config.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        if (!_joinRateLimiter.TryAcquire())
        {
            _stats.RecordJoinRateRejection();
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedProtocol(
                _logger, BaseRealtimeErrorCodes.JoinRateLimited);
            await SendErrorAsync(
                message.Ref,
                message.Channel,
                BaseRealtimeErrorCodes.JoinRateLimited,
                "The connection exceeded its channel join rate limit.",
                cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        await RemoveCompletedChannelsAsync().ConfigureAwait(false);

        if (_channels.ContainsKey(message.Channel))
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedProtocol(
                _logger, BaseRealtimeErrorCodes.ChannelAlreadyJoined);
            await SendErrorAsync(
                message.Ref,
                message.Channel,
                BaseRealtimeErrorCodes.ChannelAlreadyJoined,
                "The channel is already joined on this connection.",
                cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        if (_channels.Count >= _options.Limits.MaxChannelsPerConnection)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedProtocol(_logger, BaseRealtimeErrorCodes.TooManyChannels);
            await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.TooManyChannels, "The connection has reached the channel limit.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        if (message.Config.Kind != BaseRealtimeChannelKinds.RecordChanges)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedProtocol(_logger, BaseRealtimeErrorCodes.ChannelUnsupported);
            await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ChannelUnsupported, "The requested channel kind is not supported.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        if (message.Config.Private && _options.RequireAuthenticatedPrivateChannels && _principal.AuthenticationState == PrincipalAuthenticationState.Anonymous)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedPolicy(_logger, BaseRealtimeErrorCodes.AuthRequired);
            await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.AuthRequired, "Authentication is required for private realtime channels.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        if (!TenantRequestAllowed(message.Config.TenantId))
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketJoinRejectedPolicy(_logger, BaseRealtimeErrorCodes.ChannelUnauthorized);
            await SendErrorAsync(message.Ref, message.Channel, BaseRealtimeErrorCodes.ChannelUnauthorized, "The requested tenant is not authorized for this realtime channel.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "rejected");
            return;
        }

        var operation = new OperationContext
        {
            Operation = BaseOperationKind.RealtimeSubscribe,
            CollectionId = message.Config.CollectionId ?? "*",
            RecordId = message.Config.RecordId,
            TenantId = message.Config.TenantId ?? _principal.CurrentTenantId,
            Mode = OperationMode.User,
            CorrelationId = message.Ref,
            Now = _timeProvider.GetUtcNow()
        };

        var opened = await _feeds.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = message.Channel,
            Join = message.Config,
            Principal = _principal,
            Operation = operation
        }, cancellationToken).ConfigureAwait(false);

        if (!opened.Succeeded || opened.Value is null)
        {
            await SendErrorAsync(message.Ref, message.Channel, opened.Error?.Code ?? BaseRealtimeErrorCodes.CapabilityUnavailable, opened.Error?.Message ?? "Realtime channel could not be opened.", cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "error");
            return;
        }

        var owner = new BaseRealtimeChannelOwner(
            message.Channel,
            opened.Value.Items,
            _options.Limits.OutboundCapacity,
            cancellationToken,
            SendEventAsync,
            HandleChannelFailureAsync,
            TerminateSlowConsumerAsync);
        _channels.Add(message.Channel, owner);
        try
        {
            await SendAsync(new BaseRealtimeServerMessage
            {
                Type = BaseRealtimeProtocolTypes.Joined,
                Ref = message.Ref,
                Channel = message.Channel,
                Join = new BaseRealtimeChannelJoinResult
                {
                    Channel = message.Channel,
                    Kind = message.Config.Kind,
                    Replayable = opened.Value.Descriptor.Replayable,
                    Resumable = opened.Value.Descriptor.Resumable,
                    StreamId = opened.Value.Descriptor.StreamId,
                    Cursor = opened.Value.Descriptor.Cursor
                }
            }, cancellationToken).ConfigureAwait(false);
            owner.Activate();
        }
        catch
        {
            _channels.Remove(message.Channel);
            await owner.StopAsync().ConfigureAwait(false);
            throw;
        }

        HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
    }

    private async Task LeaveAsync(BaseRealtimeClientMessage message, CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRealtimeAspNetCoreTelemetry.StartLeave();
        if (message.Channel is not null && _channels.Remove(message.Channel, out var owner))
        {
            await owner.StopAsync().ConfigureAwait(false);
        }

        await SendAsync(new BaseRealtimeServerMessage
        {
            Type = BaseRealtimeProtocolTypes.Left,
            Ref = message.Ref,
            Channel = message.Channel
        }, cancellationToken).ConfigureAwait(false);
        HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
    }

    private async Task RemoveCompletedChannelsAsync()
    {
        var completed = _channels
            .Where(pair => pair.Value.IsCompleted)
            .ToArray();

        foreach (var pair in completed)
        {
            _channels.Remove(pair.Key);
            await pair.Value.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task StopChannelsAsync()
    {
        var channels = _channels.Values.ToArray();
        _channels.Clear();

        var stops = channels.Select(channel => channel.StopAsync()).ToArray();
        await Task.WhenAll(stops).ConfigureAwait(false);
    }

    private async Task HandleChannelFailureAsync(
        string channel,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is BaseRealtimeFeedException terminal)
        {
            try
            {
                await SendErrorAsync(
                    null,
                    channel,
                    terminal.Code,
                    terminal.SafeMessage,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BaseRealtimeSendTimeoutException)
            {
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return;
        }

        HPDBaseRealtimeAspNetCoreLog.WebSocketReceiveFailed(
            _logger, "unexpected", BaseRealtimeErrorCodes.CapabilityUnavailable);
    }

    private async Task TerminateSlowConsumerAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        _stats.RecordSlowConsumerTermination();
        HPDBaseRealtimeAspNetCoreLog.SlowConsumerTerminated(
            _logger, BaseRealtimeErrorCodes.ConsumerSlow);

        try
        {
            await SendErrorAsync(
                null,
                channel,
                BaseRealtimeErrorCodes.ConsumerSlow,
                "The realtime channel was terminated because the consumer was too slow.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (BaseRealtimeSendTimeoutException)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<BaseRealtimeClientMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(_options.Limits.MaxMessageBytes, 128 * 1024)];
        var segment = new ArraySegment<byte>(buffer);
        using var stream = new MemoryStream();

        while (true)
        {
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(_options.Limits.ReceiveIdleTimeoutSeconds));
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(segment, receiveTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _stats.RecordReceiveIdleTimeout();
                HPDBaseRealtimeAspNetCoreLog.ConnectionIdleTimedOut(_logger, BaseRealtimeErrorCodes.ConnectionIdleTimeout);
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, BaseRealtimeErrorCodes.ConnectionIdleTimeout, CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (WebSocketException)
            {
                HPDBaseRealtimeAspNetCoreLog.WebSocketReceiveFailed(
                    _logger, "transport", BaseRealtimeErrorCodes.CapabilityUnavailable);
                throw;
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (_socket.State == WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
                return new BaseRealtimeClientMessage { Type = NonTextMessageType };

            if (stream.Length + result.Count > _options.Limits.MaxMessageBytes)
            {
                _stats.RecordPayloadLimitDrop();
                HPDBaseRealtimeAspNetCoreLog.PayloadDropped(
                    _logger,
                    BaseRealtimeErrorCodes.PayloadTooLarge,
                    HPDBaseTelemetryBuckets.PayloadSize(stream.Length + result.Count));
                while (!result.EndOfMessage)
                {
                    result = await _socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
                }
                return new BaseRealtimeClientMessage { Type = PayloadTooLargeMessageType };
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        HPDBaseRealtimeAspNetCoreTelemetry.RecordReceived(stream.Length);
        try
        {
            _ = _json;
            return JsonSerializer.Deserialize(json, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
        }
        catch (JsonException)
        {
            return new BaseRealtimeClientMessage { Type = InvalidJsonMessageType };
        }
    }

    private Task SendErrorAsync(string? @ref, string? channel, string code, string message, CancellationToken cancellationToken) =>
        SendAsync(new BaseRealtimeServerMessage
        {
            Type = BaseRealtimeProtocolTypes.Error,
            Ref = @ref,
            Channel = channel,
            Error = new BaseRealtimeError
            {
                Code = code,
                Message = message
            }
        }, cancellationToken);

    private async Task SendEventAsync(string channel, BaseRealtimeEvent evt, CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRealtimeAspNetCoreTelemetry.StartSend("recordChanges");
        var message = new BaseRealtimeServerMessage
        {
            Type = BaseRealtimeProtocolTypes.Event,
            Channel = channel,
            Event = evt
        };

        var serializedLength = SerializedLength(message);
        if (serializedLength <= _options.Limits.MaxPayloadBytes)
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
            return;
        }

        if (evt.Before is not null || evt.After is not null)
        {
            message = message with
            {
                Event = evt with
                {
                    Before = null,
                    After = null
                }
            };

            var reducedLength = SerializedLength(message);
            if (reducedLength <= _options.Limits.MaxPayloadBytes)
            {
                _stats.RecordPayloadLimitDrop();
                HPDBaseRealtimeAspNetCoreLog.PayloadDropped(
                    _logger,
                    BaseRealtimeErrorCodes.PayloadTooLarge,
                    HPDBaseTelemetryBuckets.PayloadSize(serializedLength));
                await SendAsync(message, cancellationToken).ConfigureAwait(false);
                HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
                return;
            }
        }

        _stats.RecordPayloadLimitDrop();
        HPDBaseRealtimeAspNetCoreLog.PayloadDropped(
            _logger,
            BaseRealtimeErrorCodes.PayloadTooLarge,
            HPDBaseTelemetryBuckets.PayloadSize(serializedLength));
        if (evt.Cursor is not null)
        {
            HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "error");
            throw new BaseRealtimeFeedException(
                BaseRealtimeErrorCodes.PayloadTooLarge,
                "The durable realtime event exceeded the configured payload limit.");
        }

        HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "dropped");
    }

    private static int SerializedLength(BaseRealtimeServerMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage).Length;

    private bool TenantRequestAllowed(string? requestedTenantId)
    {
        if (string.IsNullOrWhiteSpace(requestedTenantId)
            || _principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System)
            return true;

        if (string.Equals(_principal.CurrentTenantId, requestedTenantId, StringComparison.Ordinal))
            return true;

        return _principal.TenantMemberships?.Any(membership => string.Equals(membership.TenantId, requestedTenantId, StringComparison.Ordinal)) == true;
    }

    private async Task SendAsync(BaseRealtimeServerMessage message, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
            return;

        _ = _json;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage);
        HPDBaseRealtimeAspNetCoreTelemetry.RecordSent(bytes.LongLength);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.Limits.SendTimeoutSeconds),
            _timeProvider);
        using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var lockTaken = false;
        try
        {
            await _sendLock.WaitAsync(sendCancellation.Token).ConfigureAwait(false);
            lockTaken = true;
            try
            {
                await _socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    sendCancellation.Token).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                _stats.RecordSendFailure();
                HPDBaseRealtimeAspNetCoreLog.WebSocketSendFailed(
                    _logger, "transport", BaseRealtimeErrorCodes.CapabilityUnavailable);
                throw;
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new BaseRealtimeSendTimeoutException();
        }
        finally
        {
            if (lockTaken)
                _sendLock.Release();
        }
    }

    private static string ChannelKindValue(string? value) => value switch
    {
        BaseRealtimeChannelKinds.RecordChanges => "recordChanges",
        _ => "unknown"
    };
}
