using System.Net.WebSockets;
using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.Logging;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeWebSocketSession
{
    private readonly WebSocket _socket;
    private readonly IBaseRealtimeFeedSource _feeds;
    private readonly BaseRealtimeOptions _options;
    private readonly BaseRealtimeStats _stats;
    private readonly PrincipalContext _principal;
    private readonly ILogger<BaseRealtimeWebSocketSession> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly BaseRealtimeJoinRateLimiter _joinRateLimiter;
    private readonly BaseRealtimeLiveQueryTransport? _liveQueries;
    private readonly BaseSubjectLifecycleHintHub? _lifecycleHints;
    private readonly IBaseSubjectLifecycleRuntime? _lifecycleRuntime;
    private readonly BaseSubjectLifecycleRegistry? _lifecycleConsumers;
    private readonly BaseSubjectContractRegistry? _subjectContracts;
    private readonly IBaseSessionFactory? _sessionFactory;
    private readonly Dictionary<string, ActiveChannel> _channels = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private readonly string _connectionEpoch = Guid.NewGuid().ToString("N");

    /// <summary>Initializes one version 2 WebSocket session.</summary>
    public BaseRealtimeWebSocketSession(
        WebSocket socket,
        IBaseRealtimeFeedSource feeds,
        JsonSerializerOptions json,
        BaseRealtimeOptions options,
        BaseRealtimeStats stats,
        PrincipalContext principal,
        ILogger<BaseRealtimeWebSocketSession> logger,
        TimeProvider timeProvider,
        BaseRealtimeLiveQueryTransport? liveQueries = null,
        BaseSubjectLifecycleHintHub? lifecycleHints = null,
        IBaseSubjectLifecycleRuntime? lifecycleRuntime = null,
        BaseSubjectLifecycleRegistry? lifecycleConsumers = null,
        BaseSubjectContractRegistry? subjectContracts = null,
        IBaseSessionFactory? sessionFactory = null)
    {
        _socket = socket;
        _feeds = feeds;
        _ = json;
        _options = options;
        _stats = stats;
        _principal = principal;
        _logger = logger;
        _timeProvider = timeProvider;
        _liveQueries = liveQueries;
        _lifecycleHints = lifecycleHints;
        _lifecycleRuntime = lifecycleRuntime;
        _lifecycleConsumers = lifecycleConsumers;
        _subjectContracts = subjectContracts;
        _sessionFactory = sessionFactory;
        _joinRateLimiter = new BaseRealtimeJoinRateLimiter(timeProvider, options.Limits.MaxJoinsPerSecond);
    }

    /// <summary>Runs the closed version 2 protocol until the connection terminates.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        HPDBaseRealtimeAspNetCoreLog.ConnectionOpened(_logger);
        try
        {
            await SendAsync(new BaseRealtimeWelcomeMessage
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                HeartbeatIntervalMs = Math.Min(_options.Limits.ReceiveIdleTimeoutSeconds * 500, 30_000),
                MaxInboundBytes = Math.Min(_options.Limits.MaxMessageBytes, 1024 * 1024),
                MaxChannels = Math.Min(_options.Limits.MaxChannelsPerConnection, 128)
            }, cancellationToken).ConfigureAwait(false);

            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                ReceiveResult received;
                try { received = await ReceiveAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(_options.Limits.ReceiveIdleTimeoutSeconds), _timeProvider, cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    HPDBaseRealtimeAspNetCoreLog.ConnectionIdleTimedOut(_logger, BaseRealtimeErrorCodes.ConnectionIdleTimeout);
                    await CloseProtocolAsync(BaseRealtimeErrorCodes.ConnectionIdleTimeout, cancellationToken).ConfigureAwait(false);
                    break;
                }
                if (received.Closed)
                    break;
                if (received.ErrorCode is not null)
                {
                    await CloseProtocolAsync(received.ErrorCode, cancellationToken).ConfigureAwait(false);
                    break;
                }

                BaseRealtimeClientMessage message = received.Message!;
                if (message.Protocol != 2
                    || !string.Equals(message.ConnectionId, _connectionId, StringComparison.Ordinal)
                    || !string.Equals(message.ConnectionEpoch, _connectionEpoch, StringComparison.Ordinal))
                {
                    await CloseProtocolAsync(BaseRealtimeErrorCodes.ProtocolInvalid, cancellationToken).ConfigureAwait(false);
                    break;
                }

                switch (message)
                {
                    case BaseRealtimeHeartbeatMessage heartbeat:
                        if (!ValidOpaque(heartbeat.HeartbeatId)) { await CloseProtocolAsync(BaseRealtimeErrorCodes.ProtocolInvalid, cancellationToken).ConfigureAwait(false); return; }
                        await SendAsync(new BaseRealtimeHeartbeatAckMessage
                        {
                            ConnectionId = _connectionId,
                            ConnectionEpoch = _connectionEpoch,
                            HeartbeatId = heartbeat.HeartbeatId
                        }, cancellationToken).ConfigureAwait(false);
                        break;
                    case BaseRealtimeJoinMessage join:
                        await JoinAsync(join, cancellationToken).ConfigureAwait(false);
                        break;
                    case BaseRealtimeLeaveMessage leave:
                        await LeaveAsync(leave.Ref).ConfigureAwait(false);
                        break;
                }
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

    private async Task JoinAsync(BaseRealtimeJoinMessage message, CancellationToken cancellationToken)
    {
        if (!ValidOpaque(message.Ref) || !_joinRateLimiter.TryAcquire())
        {
            await SendErrorAsync(message.Ref, null, BaseRealtimeErrorCodes.ProtocolInvalid, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        await RemoveCompletedChannelsAsync().ConfigureAwait(false);
        if (_channels.ContainsKey(message.Ref))
        {
            await SendErrorAsync(message.Ref, null, BaseRealtimeErrorCodes.ChannelAlreadyJoined, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_channels.Count >= Math.Min(_options.Limits.MaxChannelsPerConnection, 128))
        {
            await SendErrorAsync(message.Ref, null, BaseRealtimeErrorCodes.TooManyChannels, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.Channel is BaseRealtimeLiveQueryJoinRequest liveQuery)
        {
            await JoinLiveQueryAsync(message.Ref, liveQuery, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (message.Channel is BaseRealtimeSubjectLifecycleHintRequest lifecycleHint)
        {
            await JoinSubjectLifecycleHintsAsync(message.Ref, lifecycleHint, cancellationToken).ConfigureAwait(false);
            return;
        }

        BaseRealtimeChannelJoinRequest join = ToFeedJoin(message.Channel);
        if (!TenantRequestAllowed(join.TenantId))
        {
            await SendErrorAsync(message.Ref, null, BaseRealtimeErrorCodes.ChannelUnauthorized, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        string channelEpoch = Guid.NewGuid().ToString("N");
        var opened = await _feeds.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = message.Ref,
            Join = join,
            Principal = _principal,
            Operation = new OperationContext
            {
                Operation = BaseOperationKind.RealtimeSubscribe,
                CollectionId = join.CollectionId ?? "*",
                RecordId = join.RecordId,
                TenantId = join.TenantId ?? _principal.CurrentTenantId,
                Mode = OperationMode.User,
                CorrelationId = message.Ref,
                Now = _timeProvider.GetUtcNow()
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!opened.Succeeded || opened.Value is null)
        {
            await SendErrorAsync(message.Ref, null, opened.Error?.Code ?? BaseRealtimeErrorCodes.CapabilityUnavailable, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        var owner = new BaseRealtimeChannelOwner(
            message.Ref,
            opened.Value.Items,
            Math.Min(_options.Limits.OutboundCapacity, 32),
            cancellationToken,
            SendEventAsync,
            HandleChannelFailureAsync,
            TerminateSlowConsumerAsync);
        var active = ActiveChannel.Record(owner, channelEpoch, join.Durable);
        _channels.Add(message.Ref, active);
        try
        {
            await SendAsync(new BaseRealtimeJoinedMessage
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                Ref = message.Ref,
                ChannelEpoch = channelEpoch,
                Delivery = join.Durable ? "durable-at-least-once" : "live-at-most-once"
            }, cancellationToken).ConfigureAwait(false);
            owner.Activate();
        }
        catch
        {
            _channels.Remove(message.Ref);
            await owner.StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task JoinSubjectLifecycleHintsAsync(string reference, BaseRealtimeSubjectLifecycleHintRequest request, CancellationToken cancellationToken)
    {
        if (_lifecycleHints is null || _lifecycleRuntime is null || _lifecycleConsumers is null || _subjectContracts is null || _sessionFactory is null
            || _principal.AuthenticationState is not (PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System)
            || _lifecycleConsumers.All.SingleOrDefault(value => value.Definition.Id == request.ConsumerId && value.Definition.Version == request.ConsumerVersion) is not { } installed
            || _subjectContracts.Find(installed.Definition.ContractId, installed.Definition.ContractVersion) is not { } contract)
        {
            await SendErrorAsync(reference, null, BaseRealtimeErrorCodes.ChannelUnauthorized, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        BaseSession session = _sessionFactory.For(_principal, options => options.ProjectId = request.ProjectId);
        BaseResult<BaseUntypedSubjectLifecyclePage> authorized = await _lifecycleRuntime.ReadUntypedAsync(session, installed, null, 1, cancellationToken).ConfigureAwait(false);
        if (authorized is not BaseSuccess<BaseUntypedSubjectLifecyclePage>)
        {
            await SendErrorAsync(reference, null, BaseRealtimeErrorCodes.ChannelUnauthorized, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        BaseOwnedSubjectScopeEvidence scope = new()
        {
            Kind = contract.Definition.Scope,
            Value = contract.Definition.Scope switch
            {
                BaseSubjectScopeKind.Global => null,
                BaseSubjectScopeKind.Tenant => _principal.CurrentTenantId,
                BaseSubjectScopeKind.Project => request.ProjectId,
                _ => null,
            },
        };
        string epoch = Guid.NewGuid().ToString("N");
        BaseSubjectLifecycleHintHub.Lease lease = _lifecycleHints.Subscribe(contract.Definition.Id, contract.Definition.Version, scope);
        var owner = new BaseRealtimeSubjectLifecycleHintOwner(lease, _lifecycleRuntime, session, installed, cancellationToken,
            (checkpoint, token) => SendAsync(new BaseRealtimeSubjectLifecycleHintMessage
            {
                ConnectionId = _connectionId, ConnectionEpoch = _connectionEpoch, Ref = reference,
                ChannelEpoch = epoch, Checkpoint = BaseSubjectReferenceEncoding.Encode(checkpoint.ToArray()),
            }, token),
            (exception, token) => HandleLifecycleHintFailureAsync(reference, epoch, exception, token));
        _channels.Add(reference, ActiveChannel.Lifecycle(owner, epoch));
        try
        {
            await SendAsync(new BaseRealtimeJoinedMessage { ConnectionId = _connectionId, ConnectionEpoch = _connectionEpoch,
                Ref = reference, ChannelEpoch = epoch, Delivery = "lifecycle-hints-non-authoritative" }, cancellationToken).ConfigureAwait(false);
            owner.Activate();
        }
        catch
        {
            _channels.Remove(reference); await owner.DisposeAsync().ConfigureAwait(false); throw;
        }
    }

    private Task HandleLifecycleHintFailureAsync(string reference, string epoch, Exception exception, CancellationToken cancellationToken) =>
        SendErrorAsync(reference, epoch,
            exception is BaseRealtimeFeedException feed ? feed.Code : BaseRealtimeErrorCodes.ReplacementRequired,
            true, cancellationToken);

    private async Task LeaveAsync(string reference)
    {
        if (!_channels.Remove(reference, out ActiveChannel? active)) return;
        await active.StopAsync().ConfigureAwait(false);
    }

    private async Task JoinLiveQueryAsync(
        string reference,
        BaseRealtimeLiveQueryJoinRequest request,
        CancellationToken cancellationToken)
    {
        if (_liveQueries is null)
        {
            await SendErrorAsync(reference, null, BaseRealtimeErrorCodes.ChannelUnsupported, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        string epoch = Guid.NewGuid().ToString("N");
        IBaseLiveQuerySubscription<JsonElement> subscription;
        try
        {
            subscription = await _liveQueries.OpenAsync(request, _principal, reference, cancellationToken).ConfigureAwait(false);
        }
        catch (BaseLiveQueryException exception)
        {
            await SendErrorAsync(reference, null, exception.Code, true, cancellationToken).ConfigureAwait(false);
            return;
        }
        var owner = new BaseRealtimeLiveQueryOwner(
            subscription,
            cancellationToken,
            (transition, token) => SendLiveQueryTransitionAsync(reference, epoch, transition, token));
        _channels.Add(reference, ActiveChannel.LiveQuery(owner, epoch));
        try
        {
            await SendAsync(new BaseRealtimeJoinedMessage
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                Ref = reference,
                ChannelEpoch = epoch,
                Delivery = "live-query-snapshots"
            }, cancellationToken).ConfigureAwait(false);
            owner.Activate();
        }
        catch
        {
            _channels.Remove(reference);
            await owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Task SendLiveQueryTransitionAsync(
        string reference,
        string epoch,
        BaseLiveQueryTransition<JsonElement> transition,
        CancellationToken cancellationToken)
    {
        if (transition.Kind == BaseLiveQueryTransitionKind.Failed)
            return SendErrorAsync(reference, epoch, transition.Failure?.Code ?? BaseLiveQueryErrorCodes.ExecutionFailed, true, cancellationToken);
        if (transition.Kind == BaseLiveQueryTransitionKind.SubjectAuthorityChanged)
            return SendAsync(new BaseRealtimeLiveQuerySubjectAuthorityChanged
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                Ref = reference,
                ChannelEpoch = epoch,
                ContractId = transition.SubjectContractId ?? throw new InvalidOperationException(BaseRealtimeErrorCodes.ProtocolInvalid),
                ContractVersion = transition.SubjectContractVersion ?? throw new InvalidOperationException(BaseRealtimeErrorCodes.ProtocolInvalid),
                StateGeneration = (transition.SubjectStateGeneration ?? throw new InvalidOperationException(BaseRealtimeErrorCodes.ProtocolInvalid)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            }, cancellationToken);
        return SendAsync(new BaseRealtimeLiveQuerySnapshotMessage
        {
            ConnectionId = _connectionId,
            ConnectionEpoch = _connectionEpoch,
            Ref = reference,
            ChannelEpoch = epoch,
            Version = transition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source = transition.Version == 1 ? "initial" : "rerun",
            Value = transition.Value
        }, cancellationToken);
    }

    private static BaseRealtimeChannelJoinRequest ToFeedJoin(BaseRealtimeChannelRequest request)
    {
        string collection;
        BaseRealtimeRecordFeedFilter filter;
        bool durable;
        string? cursor;
        switch (request)
        {
            case BaseRealtimeLiveFeedRequest live:
                collection = live.Collection; filter = live.Filter; durable = false; cursor = null; break;
            case BaseRealtimeDurableFeedRequest durableRequest:
                collection = durableRequest.Collection; filter = durableRequest.Filter; durable = true; cursor = null; break;
            case BaseRealtimeResumeFeedRequest resume:
                collection = resume.Collection; filter = resume.Filter; durable = true; cursor = resume.Cursor; break;
            default:
                throw new InvalidOperationException("The realtime channel kind is not a record feed.");
        }
        return new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = collection,
            RecordId = filter.RecordId,
            Operations = filter.Operations is null ? null : [.. filter.Operations],
            EventTypes = filter.EventTypes is null ? null : [.. filter.EventTypes],
            TenantId = filter.TenantId,
            IncludeSnapshots = filter.IncludeSnapshots,
            IncludeBefore = filter.IncludeBefore,
            Durable = durable,
            ResumeCursor = cursor
        };
    }

    private async Task SendEventAsync(string reference, BaseRealtimeEvent evt, CancellationToken cancellationToken)
    {
        if (!_channels.TryGetValue(reference, out ActiveChannel? active))
            return;
        BaseRealtimeServerMessage message;
        if (evt.SubjectAuthorityPublication is { } publication)
        {
            message = active.Durable
                ? new BaseRealtimeDurableSubjectAuthorityChanged
                {
                    ConnectionId = _connectionId,
                    ConnectionEpoch = _connectionEpoch,
                    Ref = reference,
                    ChannelEpoch = active.Epoch,
                    ContractId = publication.ContractId,
                    ContractVersion = publication.ContractVersion,
                    StateGeneration = publication.PublishedStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Cursor = evt.Cursor ?? throw new BaseRealtimeFeedException(BaseRealtimeErrorCodes.ProtocolInvalid, "A durable subject control did not contain a cursor."),
                }
                : new BaseRealtimeLiveSubjectAuthorityChanged
                {
                    ConnectionId = _connectionId,
                    ConnectionEpoch = _connectionEpoch,
                    Ref = reference,
                    ChannelEpoch = active.Epoch,
                    ContractId = publication.ContractId,
                    ContractVersion = publication.ContractVersion,
                    StateGeneration = publication.PublishedStateGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
        }
        else
        {
            message = active.Durable
                ? new BaseRealtimeDurableRecordEventMessage
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                Ref = reference,
                ChannelEpoch = active.Epoch,
                Event = evt,
                Cursor = evt.Cursor ?? throw new BaseRealtimeFeedException(BaseRealtimeErrorCodes.ProtocolInvalid, "A durable event did not contain a cursor.")
            }
            : new BaseRealtimeLiveRecordEventMessage
            {
                ConnectionId = _connectionId,
                ConnectionEpoch = _connectionEpoch,
                Ref = reference,
                ChannelEpoch = active.Epoch,
                Event = evt with { Cursor = null }
            };
        }
        if (SerializedLength(message) > _options.Limits.MaxPayloadBytes)
            throw new BaseRealtimeFeedException(BaseRealtimeErrorCodes.PayloadTooLarge, "The realtime event exceeded the configured payload limit.");
        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private Task SendErrorAsync(string? reference, string? channelEpoch, string code, bool terminal, CancellationToken cancellationToken) =>
        SendAsync(new BaseRealtimeErrorMessage
        {
            ConnectionId = _connectionId,
            ConnectionEpoch = _connectionEpoch,
            Ref = reference,
            ChannelEpoch = channelEpoch,
            Terminal = terminal,
            Error = new BaseRealtimeError { Code = code, Message = SafeMessage(code) }
        }, cancellationToken);

    private async Task CloseProtocolAsync(string code, CancellationToken cancellationToken)
    {
        await SendErrorAsync(null, null, code, true, cancellationToken).ConfigureAwait(false);
        if (_socket.State == WebSocketState.Open)
            await _socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "BASE realtime protocol failure.", cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleChannelFailureAsync(string reference, Exception exception, CancellationToken cancellationToken)
    {
        string code = exception is BaseRealtimeFeedException terminal
            ? terminal.Code
            : BaseRealtimeErrorCodes.CapabilityUnavailable;
        string? epoch = _channels.TryGetValue(reference, out ActiveChannel? active) ? active.Epoch : null;
        await SendErrorAsync(reference, epoch, code, true, cancellationToken).ConfigureAwait(false);
    }

    private Task TerminateSlowConsumerAsync(string reference, CancellationToken cancellationToken)
    {
        _stats.RecordSlowConsumerTermination();
        string? epoch = _channels.TryGetValue(reference, out ActiveChannel? active) ? active.Epoch : null;
        return SendErrorAsync(reference, epoch, BaseRealtimeErrorCodes.ConsumerSlow, true, cancellationToken);
    }

    private async Task<ReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Min(_options.Limits.MaxMessageBytes, 128 * 1024)];
        using var stream = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return new ReceiveResult(null, null, true);
            if (result.MessageType != WebSocketMessageType.Text)
                return new ReceiveResult(null, BaseRealtimeErrorCodes.ProtocolInvalid, false);
            if (stream.Length + result.Count > Math.Min(_options.Limits.MaxMessageBytes, 1024 * 1024))
                return new ReceiveResult(null, BaseRealtimeErrorCodes.PayloadTooLarge, false);
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        try
        {
            byte[] payload = stream.ToArray();
            HPDBaseRealtimeAspNetCoreTelemetry.RecordReceived(payload.Length);
            if (HasDuplicateProperties(payload)) return new ReceiveResult(null, BaseRealtimeErrorCodes.ProtocolInvalid, false);
            BaseRealtimeClientMessage? message = JsonSerializer.Deserialize(payload, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
            return message is null
                ? new ReceiveResult(null, BaseRealtimeErrorCodes.ProtocolInvalid, false)
                : new ReceiveResult(message, null, false);
        }
        catch (JsonException)
        {
            return new ReceiveResult(null, BaseRealtimeErrorCodes.ProtocolInvalid, false);
        }
    }

    private static bool HasDuplicateProperties(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && (objects.Count == 0 || !objects.Peek().Add(reader.GetString() ?? string.Empty))) return true;
        }
        return objects.Count != 0;
    }

    private async Task SendAsync(BaseRealtimeServerMessage message, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
            return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.Limits.SendTimeoutSeconds));
        await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            HPDBaseRealtimeAspNetCoreTelemetry.RecordSent(bytes.Length);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static int SerializedLength(BaseRealtimeServerMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage).Length;

    private async Task RemoveCompletedChannelsAsync()
    {
        foreach ((string key, ActiveChannel value) in _channels.Where(pair => pair.Value.IsCompleted).ToArray())
        {
            _channels.Remove(key);
            await value.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task StopChannelsAsync()
    {
        ActiveChannel[] owners = _channels.Values.ToArray();
        _channels.Clear();
        await Task.WhenAll(owners.Select(owner => owner.StopAsync())).ConfigureAwait(false);
    }

    private bool TenantRequestAllowed(string? requestedTenantId) =>
        string.IsNullOrWhiteSpace(requestedTenantId)
        || _principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System
        || string.Equals(_principal.CurrentTenantId, requestedTenantId, StringComparison.Ordinal)
        || _principal.TenantMemberships?.Any(item => string.Equals(item.TenantId, requestedTenantId, StringComparison.Ordinal)) == true;

    private static bool ValidOpaque(string value) => value.Length is >= 1 and <= 128 && value.All(character => character is >= '!' and <= '~');
    private static string SafeMessage(string code) => code switch
    {
        BaseRealtimeErrorCodes.PayloadTooLarge => "The realtime payload exceeded the configured limit.",
        BaseRealtimeErrorCodes.ChannelUnauthorized => "The realtime channel is not authorized.",
        BaseRealtimeErrorCodes.TooManyChannels => "The connection reached its channel limit.",
        BaseRealtimeErrorCodes.ConsumerSlow => "The realtime channel consumer was too slow.",
        _ => "The realtime operation failed."
    };

    private sealed class ActiveChannel
    {
        private readonly BaseRealtimeChannelOwner? _record;
        private readonly BaseRealtimeLiveQueryOwner? _liveQuery;
        private readonly BaseRealtimeSubjectLifecycleHintOwner? _lifecycle;
        private ActiveChannel(BaseRealtimeChannelOwner? record, BaseRealtimeLiveQueryOwner? liveQuery, BaseRealtimeSubjectLifecycleHintOwner? lifecycle, string epoch, bool durable)
        { _record = record; _liveQuery = liveQuery; _lifecycle = lifecycle; Epoch = epoch; Durable = durable; }
        internal string Epoch { get; }
        internal bool Durable { get; }
        internal bool IsCompleted => _record?.IsCompleted ?? _liveQuery?.IsCompleted ?? _lifecycle?.IsCompleted ?? true;
        internal static ActiveChannel Record(BaseRealtimeChannelOwner owner, string epoch, bool durable) => new(owner, null, null, epoch, durable);
        internal static ActiveChannel LiveQuery(BaseRealtimeLiveQueryOwner owner, string epoch) => new(null, owner, null, epoch, false);
        internal static ActiveChannel Lifecycle(BaseRealtimeSubjectLifecycleHintOwner owner, string epoch) => new(null, null, owner, epoch, false);
        internal Task StopAsync() => _record is not null ? _record.StopAsync() : _liveQuery is not null ? _liveQuery.DisposeAsync().AsTask() : _lifecycle!.DisposeAsync().AsTask();
    }
    private sealed record ReceiveResult(BaseRealtimeClientMessage? Message, string? ErrorCode, bool Closed);
}
