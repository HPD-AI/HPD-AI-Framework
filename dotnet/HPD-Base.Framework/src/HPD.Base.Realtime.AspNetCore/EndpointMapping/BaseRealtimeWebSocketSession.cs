using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Base.Realtime.AspNetCore.Observability;
using HPD.Base.Realtime.AspNetCore.Observability.Logging;
using HPD.Base.Observability;
using HPD.Base.Runtime;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.Feeds;
using HPD.Base.Realtime.Serialization;
using HPD.Events;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Realtime.AspNetCore.EndpointMapping;

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
    private readonly Dictionary<string, CancellationTokenSource> _channels = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public BaseRealtimeWebSocketSession(
        WebSocket socket,
        IBaseRealtimeFeedSource feeds,
        JsonSerializerOptions json,
        BaseRealtimeOptions options,
        BaseRealtimeStats stats,
        PrincipalContext principal,
        ILogger<BaseRealtimeWebSocketSession> logger)
    {
        _socket = socket;
        _feeds = feeds;
        _json = json;
        _options = options;
        _stats = stats;
        _principal = principal;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _stats.RecordConnectionOpened();
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
            foreach (var channel in _channels.Values)
                channel.Cancel();
            foreach (var channel in _channels.Values)
                channel.Dispose();
            _sendLock.Dispose();
            _stats.RecordConnectionClosed();
        }
    }

    private async Task HandleAsync(BaseRealtimeClientMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case BaseRealtimeProtocolTypes.Connect:
                await SendAsync(new BaseRealtimeServerMessage { Type = BaseRealtimeProtocolTypes.Connected, Ref = message.Ref }, cancellationToken).ConfigureAwait(false);
                break;
            case BaseRealtimeProtocolTypes.Authenticate:
                await SendAsync(new BaseRealtimeServerMessage { Type = BaseRealtimeProtocolTypes.System, Ref = message.Ref }, cancellationToken).ConfigureAwait(false);
                break;
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
            Now = DateTimeOffset.UtcNow
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

        var channelCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _channels[message.Channel] = channelCancellation;
        _ = Task.Run(() => PumpChannelAsync(message.Channel, opened.Value.Items, channelCancellation.Token), CancellationToken.None);

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
                StreamId = opened.Value.Descriptor.StreamId
            }
        }, cancellationToken).ConfigureAwait(false);
        HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
    }

    private async Task LeaveAsync(BaseRealtimeClientMessage message, CancellationToken cancellationToken)
    {
        using var activity = HPDBaseRealtimeAspNetCoreTelemetry.StartLeave();
        if (message.Channel is not null && _channels.Remove(message.Channel, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        await SendAsync(new BaseRealtimeServerMessage
        {
            Type = BaseRealtimeProtocolTypes.Left,
            Ref = message.Ref,
            Channel = message.Channel
        }, cancellationToken).ConfigureAwait(false);
        HPDBaseRealtimeAspNetCoreTelemetry.Finish(activity, "ok");
    }

    private async Task PumpChannelAsync(string channel, IAsyncEnumerable<BaseRealtimeEvent> events, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await SendEventAsync(channel, evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            _stats.RecordSendFailure();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            HPDBaseRealtimeAspNetCoreLog.WebSocketReceiveFailed(
                _logger, "unexpected", BaseRealtimeErrorCodes.CapabilityUnavailable);
            throw;
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
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.Limits.HeartbeatTimeoutSeconds)));
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(segment, receiveTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _stats.RecordHeartbeatTimeout();
                HPDBaseRealtimeAspNetCoreLog.HeartbeatTimedOut(_logger, BaseRealtimeErrorCodes.HeartbeatTimeout);
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, BaseRealtimeErrorCodes.HeartbeatTimeout, CancellationToken.None).ConfigureAwait(false);
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
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                HPDBaseRealtimeAspNetCoreLog.WebSocketSendFailed(
                    _logger, "transport", BaseRealtimeErrorCodes.CapabilityUnavailable);
                throw;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static string ChannelKindValue(string? value) => value switch
    {
        BaseRealtimeChannelKinds.RecordChanges => "recordChanges",
        _ => "unknown"
    };
}
