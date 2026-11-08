using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>Options for a protocol-v2 client-tool-provider WebSocket connection.</summary>
public sealed record HpdAgentClientToolProviderOptions(Uri Endpoint)
{
    public required HpdAgentClientToolProviderIdentity Identity { get; init; }
    public HpdAgentClientAppProvider? AppProvider { get; init; }
    public IReadOnlyList<JsonElement>? ClientToolHarnesses { get; init; }
    public JsonElement? Context { get; init; }
    public string Readiness { get; init; } = "Ready";
    public JsonElement? Metadata { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional socket factory used by tests and custom transports.</summary>
    public Func<WebSocket>? WebSocketFactory { get; init; }
}

public sealed record HpdAgentClientToolProviderIdentity(
    string ProviderName,
    string AppKind,
    string? InstanceId = null,
    string? InstallationId = null,
    string? UserHint = null,
    string? Origin = null,
    string? Version = null);

public sealed record HpdAgentClientAppProvider(
    string Name,
    string? DisplayName = null,
    string? Description = null,
    string? Version = null,
    IReadOnlyList<string>? Tags = null,
    JsonElement? Metadata = null);

public sealed record HpdAgentClientToolProviderWelcome(
    string ClientRuntimeId,
    string ConnectionId,
    int HeartbeatIntervalMs);

public sealed record HpdAgentClientToolProviderError(
    string Code,
    string Message);

public sealed record HpdAgentClientToolProviderInvocation(
    string ClientRuntimeId,
    string ConnectionId,
    string BindingId,
    string InvocationId,
    string RequestId,
    string ToolName,
    string VisibleToolName,
    string CallId,
    JsonElement Arguments,
    string? ClientOperationId,
    JsonElement? Operation,
    JsonElement? ExpectedContext,
    string? RequestedInvocationMode,
    string ResolvedInvocationMode,
    string? Deadline);

/// <summary>Immediate outcome returned for a provider invocation.</summary>
public sealed record HpdAgentClientToolProviderOutcome
{
    public string Outcome { get; init; } = "Completed";
    public IReadOnlyList<JsonElement>? Content { get; init; }
    public JsonElement? Error { get; init; }
    public string? ClientOperationId { get; init; }
    public JsonElement? OperationKind { get; init; }
    public JsonElement? OperationCapabilities { get; init; }
    public JsonElement? Augmentation { get; init; }

    public static HpdAgentClientToolProviderOutcome Completed(
        IReadOnlyList<JsonElement>? content = null) =>
        new() { Outcome = "Completed", Content = content };

    public static HpdAgentClientToolProviderOutcome Failed(string message) =>
        new()
        {
            Outcome = "Failed",
            Error = ErrorElement("providerFailure", message)
        };

    public static HpdAgentClientToolProviderOutcome Rejected(string message) =>
        new()
        {
            Outcome = "Rejected",
            Error = ErrorElement("providerRejected", message)
        };

    private static JsonElement ErrorElement(string kind, string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}

/// <summary>Terminal outcome for provider-owned background work.</summary>
public sealed record HpdAgentClientToolProviderOperationOutcome(
    string BindingId,
    string ClientOperationId,
    string State,
    IReadOnlyList<JsonElement>? Content = null,
    JsonElement? Augmentation = null,
    JsonElement? Error = null,
    string? CancellationReason = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Protocol-v2 client-tool-provider connection. The client sends hello,
/// optionally publishes a manifest, maintains heartbeats, dispatches invokes,
/// and posts typed outcomes back over the same socket.
/// </summary>
public sealed class HpdAgentClientToolProviderClient : IAsyncDisposable
{
    private const int MaximumMessageBytes = 1024 * 1024;
    private readonly HpdAgentClientToolProviderOptions _options;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _sync = new();
    private WebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private Func<HpdAgentClientToolProviderInvocation, CancellationToken, ValueTask<HpdAgentClientToolProviderOutcome>>? _invokeHandler;
    private Action<HpdAgentClientToolProviderError>? _serverErrorHandler;
    private Action<Exception>? _errorHandler;
    private Action? _disconnectedHandler;

    public HpdAgentClientToolProviderClient(HpdAgentClientToolProviderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.Endpoint.IsAbsoluteUri)
            throw new ArgumentException("Provider endpoint must be absolute.", nameof(options));
    }

    public string? ClientRuntimeId { get; private set; }
    public string? ConnectionId { get; private set; }
    public int HeartbeatIntervalMs { get; private set; }
    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public HpdAgentEventSubscription OnInvoke(
        Func<HpdAgentClientToolProviderInvocation, CancellationToken, ValueTask<HpdAgentClientToolProviderOutcome>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
            _invokeHandler = handler;
        return new HpdAgentEventSubscription(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_invokeHandler, handler))
                    _invokeHandler = null;
            }
        });
    }

    public HpdAgentEventSubscription OnServerError(Action<HpdAgentClientToolProviderError> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
            _serverErrorHandler = handler;
        return new HpdAgentEventSubscription(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_serverErrorHandler, handler))
                    _serverErrorHandler = null;
            }
        });
    }

    public HpdAgentEventSubscription OnError(Action<Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
            _errorHandler = handler;
        return new HpdAgentEventSubscription(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_errorHandler, handler))
                    _errorHandler = null;
            }
        });
    }

    public HpdAgentEventSubscription OnDisconnected(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_sync)
            _disconnectedHandler = handler;
        return new HpdAgentEventSubscription(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_disconnectedHandler, handler))
                    _disconnectedHandler = null;
            }
        });
    }

    public async Task<HpdAgentClientToolProviderWelcome> ConnectAsync(
        WebSocket? socket = null,
        CancellationToken cancellationToken = default)
    {
        if (_socket is not null)
            throw new InvalidOperationException("The provider connection is already active.");
        socket ??= _options.WebSocketFactory?.Invoke() ?? new ClientWebSocket();
        if (socket.State != WebSocketState.Open)
        {
            if (socket is not ClientWebSocket clientWebSocket)
                throw new InvalidOperationException(
                    "A custom WebSocket must already be open before ConnectAsync is called.");
            foreach (var header in _options.Headers)
                clientWebSocket.Options.SetRequestHeader(header.Key, header.Value);
            await clientWebSocket.ConnectAsync(
                WebSocketEndpoint(_options.Endpoint),
                cancellationToken).ConfigureAwait(false);
        }

        _socket = socket;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await SendHelloAsync(cancellationToken).ConfigureAwait(false);
            var welcomeMessage = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false)
                ?? throw new HpdAgentStreamException(
                    "Provider connection closed before the welcome message.");
            using var welcomeDocument = JsonDocument.Parse(welcomeMessage);
            var root = welcomeDocument.RootElement;
            var type = ReadString(root, "type");
            if (type == "provider.error")
                throw new HpdAgentStreamException(ReadString(root, "message") ?? "Provider handshake failed.");
            if (type != "provider.welcome")
                throw new HpdAgentStreamException(
                    $"Expected provider.welcome, received '{type ?? "<missing>"}'.");

            var clientRuntimeId = ReadString(root, "clientRuntimeId")
                ?? throw new HpdAgentStreamException("Provider welcome did not include clientRuntimeId.");
            var connectionId = ReadString(root, "connectionId")
                ?? throw new HpdAgentStreamException("Provider welcome did not include connectionId.");
            var heartbeat = root.TryGetProperty("heartbeatIntervalMs", out var heartbeatElement) &&
                            heartbeatElement.TryGetInt32(out var interval)
                ? interval
                : 15_000;
            var welcome = new HpdAgentClientToolProviderWelcome(
                clientRuntimeId,
                connectionId,
                heartbeat);
            ClientRuntimeId = clientRuntimeId;
            ConnectionId = connectionId;
            HeartbeatIntervalMs = Math.Max(100, heartbeat);

            if (_options.AppProvider is not null)
                await SendManifestAsync(cancellationToken).ConfigureAwait(false);

            _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
            _heartbeatLoop = HeartbeatLoopAsync(_lifetime.Token);
            return welcome;
        }
        catch
        {
            await DisposeSocketAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task PublishManifestAsync(
        HpdAgentClientAppProvider appProvider,
        IReadOnlyList<JsonElement>? harnesses = null,
        JsonElement? context = null,
        string readiness = "Ready",
        JsonElement? metadata = null,
        CancellationToken cancellationToken = default) =>
        SendManifestCoreAsync(
            appProvider,
            harnesses,
            context,
            readiness,
            metadata,
            cancellationToken);

    public Task SendHeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.heartbeat");
            writer.WriteEndObject();
        }, cancellationToken);

    public Task SendOperationOutcomeAsync(
        HpdAgentClientToolProviderOperationOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.backgroundOperationOutcome");
            writer.WriteString("bindingId", outcome.BindingId);
            writer.WriteString("clientOperationId", outcome.ClientOperationId);
            writer.WriteString("state", outcome.State);
            WriteArray(writer, "content", outcome.Content);
            WriteProperty(writer, "augmentation", outcome.Augmentation);
            WriteProperty(writer, "error", outcome.Error);
            WriteString(writer, "cancellationReason", outcome.CancellationReason);
            if (outcome.Metadata is not null)
            {
                writer.WritePropertyName("metadata");
                writer.WriteStartObject();
                foreach (var item in outcome.Metadata)
                    writer.WriteString(item.Key, item.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }, cancellationToken);
    }

    public Task SendReleaseAsync(
        string? bindingId = null,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.release");
            WriteString(writer, "bindingId", bindingId);
            WriteString(writer, "reason", reason);
            writer.WriteEndObject();
        }, cancellationToken);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket is null)
            return;
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await SendReleaseAsync(reason: "Client closed.", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client connection closed.",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                socket.Abort();
            }
        }
        await DisposeSocketAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    private Task SendManifestAsync(CancellationToken cancellationToken) =>
        SendManifestCoreAsync(
            _options.AppProvider!,
            _options.ClientToolHarnesses,
            _options.Context,
            _options.Readiness,
            _options.Metadata,
            cancellationToken);

    private Task SendManifestCoreAsync(
        HpdAgentClientAppProvider appProvider,
        IReadOnlyList<JsonElement>? harnesses,
        JsonElement? context,
        string readiness,
        JsonElement? metadata,
        CancellationToken cancellationToken) =>
        SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.manifest");
            writer.WriteString("protocolVersion", "2");
            writer.WritePropertyName("appProvider");
            WriteAppProvider(writer, appProvider);
            WriteProperty(writer, "context", context);
            writer.WriteString("readiness", readiness);
            WriteArray(writer, "clientToolHarnesses", harnesses);
            WriteProperty(writer, "metadata", metadata);
            writer.WriteEndObject();
        }, cancellationToken);

    private Task SendHelloAsync(CancellationToken cancellationToken) =>
        SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.hello");
            writer.WriteString("protocolVersion", "2");
            writer.WritePropertyName("identity");
            WriteIdentity(writer, _options.Identity);
            writer.WriteEndObject();
        }, cancellationToken);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var text = await ReceiveTextAsync(_socket!, cancellationToken).ConfigureAwait(false);
                if (text is null)
                    break;
                await HandleMessageAsync(text, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            NotifyError(exception);
        }
        finally
        {
            NotifyDisconnected();
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                await Task.Delay(HeartbeatIntervalMs, cancellationToken).ConfigureAwait(false);
                await SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            NotifyError(exception);
        }
    }

    private async Task HandleMessageAsync(string text, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var type = ReadString(root, "type");
        switch (type)
        {
            case "provider.invoke":
                await HandleInvokeAsync(root, cancellationToken).ConfigureAwait(false);
                break;
            case "provider.error":
                NotifyServerError(new HpdAgentClientToolProviderError(
                    ReadString(root, "code") ?? "provider_error",
                    ReadString(root, "message") ?? "The provider server returned an error."));
                break;
            case "provider.welcome":
                break;
            default:
                NotifyError(new InvalidDataException(
                    $"Unsupported server-to-provider message type '{type ?? "<missing>"}'."));
                break;
        }
    }

    private async Task HandleInvokeAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var invocation = new HpdAgentClientToolProviderInvocation(
            ReadString(root, "clientRuntimeId") ?? ClientRuntimeId ?? string.Empty,
            ReadString(root, "connectionId") ?? ConnectionId ?? string.Empty,
            ReadString(root, "bindingId") ?? string.Empty,
            ReadString(root, "invocationId") ?? string.Empty,
            ReadString(root, "requestId") ?? string.Empty,
            ReadString(root, "toolName") ?? string.Empty,
            ReadString(root, "visibleToolName") ?? string.Empty,
            ReadString(root, "callId") ?? string.Empty,
            root.TryGetProperty("arguments", out var arguments)
                ? arguments.Clone()
                : EmptyObject(),
            ReadString(root, "clientOperationId"),
            CloneProperty(root, "operation"),
            CloneProperty(root, "expectedContext"),
            ReadString(root, "requestedInvocationMode"),
            ReadString(root, "resolvedInvocationMode") ?? "Synchronous",
            ReadString(root, "deadline"));

        HpdAgentClientToolProviderOutcome outcome;
        try
        {
            var handler = _invokeHandler;
            outcome = handler is null
                ? HpdAgentClientToolProviderOutcome.Failed(
                    $"No provider invoke handler is registered for '{invocation.ToolName}'.")
                : await handler(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            outcome = HpdAgentClientToolProviderOutcome.Failed(exception.Message);
        }

        await SendOutcomeAsync(invocation, outcome, cancellationToken).ConfigureAwait(false);
    }

    private Task SendOutcomeAsync(
        HpdAgentClientToolProviderInvocation invocation,
        HpdAgentClientToolProviderOutcome outcome,
        CancellationToken cancellationToken) =>
        SendAsync(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "provider.invokeOutcome");
            writer.WriteString("bindingId", invocation.BindingId);
            writer.WriteString("invocationId", invocation.InvocationId);
            writer.WriteString("requestId", invocation.RequestId);
            writer.WriteString("outcome", outcome.Outcome);
            WriteArray(writer, "content", outcome.Content);
            WriteProperty(writer, "error", outcome.Error);
            WriteString(writer, "clientOperationId", outcome.ClientOperationId);
            WriteProperty(writer, "operationKind", outcome.OperationKind);
            WriteProperty(writer, "operationCapabilities", outcome.OperationCapabilities);
            WriteProperty(writer, "augmentation", outcome.Augmentation);
            writer.WriteEndObject();
        }, cancellationToken);

    private async Task SendAsync(
        Action<Utf8JsonWriter> write,
        CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket?.State != WebSocketState.Open)
            throw new InvalidOperationException("The provider WebSocket is not connected.");
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                write(writer);
            await socket.SendAsync(
                stream.ToArray(),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<string?> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Provider messages must be UTF-8 text frames.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaximumMessageBytes)
                throw new InvalidDataException("Provider message exceeded the one-megabyte limit.");
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private async ValueTask DisposeSocketAsync()
    {
        _lifetime?.Cancel();
        var socket = _socket;
        _socket = null;
        socket?.Dispose();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_heartbeatLoop is not null)
        {
            try
            {
                await _heartbeatLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _lifetime?.Dispose();
        _lifetime = null;
        _receiveLoop = null;
        _heartbeatLoop = null;
    }

    private void NotifyError(Exception exception)
    {
        Action<Exception>? handler;
        lock (_sync)
            handler = _errorHandler;
        handler?.Invoke(exception);
    }

    private void NotifyServerError(HpdAgentClientToolProviderError error)
    {
        Action<HpdAgentClientToolProviderError>? handler;
        lock (_sync)
            handler = _serverErrorHandler;
        handler?.Invoke(error);
    }

    private void NotifyDisconnected()
    {
        Action? handler;
        lock (_sync)
            handler = _disconnectedHandler;
        handler?.Invoke();
    }

    private static Uri WebSocketEndpoint(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint);
        builder.Scheme = endpoint.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" or "wss" => endpoint.Scheme,
            _ => throw new ArgumentException(
                "Provider endpoint must use http, https, ws, or wss.",
                nameof(endpoint))
        };
        return builder.Uri;
    }

    private static void WriteIdentity(Utf8JsonWriter writer, HpdAgentClientToolProviderIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("providerName", identity.ProviderName);
        writer.WriteString("appKind", identity.AppKind);
        WriteString(writer, "instanceId", identity.InstanceId);
        WriteString(writer, "installationId", identity.InstallationId);
        WriteString(writer, "userHint", identity.UserHint);
        WriteString(writer, "origin", identity.Origin);
        WriteString(writer, "version", identity.Version);
        writer.WriteEndObject();
    }

    private static void WriteAppProvider(Utf8JsonWriter writer, HpdAgentClientAppProvider provider)
    {
        writer.WriteStartObject();
        writer.WriteString("name", provider.Name);
        WriteString(writer, "displayName", provider.DisplayName);
        WriteString(writer, "description", provider.Description);
        WriteString(writer, "version", provider.Version);
        if (provider.Tags is not null)
        {
            writer.WritePropertyName("tags");
            writer.WriteStartArray();
            foreach (var tag in provider.Tags)
                writer.WriteStringValue(tag);
            writer.WriteEndArray();
        }
        WriteProperty(writer, "metadata", provider.Metadata);
        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            writer.WriteString(name, value);
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, JsonElement? value)
    {
        if (value is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
            return;
        writer.WritePropertyName(name);
        element.WriteTo(writer);
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<JsonElement>? values)
    {
        if (values is null)
            return;
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
            value.WriteTo(writer);
        writer.WriteEndArray();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? CloneProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.Clone() : null;

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
