// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HPD.Agent.ClientTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Endpoints for live client tool provider connections.
/// </summary>
internal static class ClientToolProviderEndpoints
{
    private const string ProviderConnectPath = "/client-tool-providers/connect";

    /// <summary>
    /// Maps client tool provider endpoints.
    /// </summary>
    public static void Map(IEndpointRouteBuilder endpoints, IClientToolProviderRegistry registry)
    {
        endpoints.MapGet("/client-tool-providers", (
                string? appProviderName,
                string? appKind,
                bool includeDisconnected) =>
                ListProviders(registry, appProviderName, appKind, includeDisconnected))
            .WithName("ListClientToolProviders")
            .WithSummary("List connected client tool providers");

        endpoints.MapGet("/client-tool-providers/{clientRuntimeId}", (string clientRuntimeId) =>
                GetProvider(registry, clientRuntimeId))
            .WithName("GetClientToolProvider")
            .WithSummary("Get one connected client tool provider");

        endpoints.MapGet(ProviderConnectPath, (HttpContext context, CancellationToken ct) =>
                ConnectAsync(context, registry, ct))
            .WithName("ConnectClientToolProvider")
            .WithSummary("Connect a live client tool provider over WebSocket");
    }

    private static Ok<IReadOnlyList<ClientToolProviderSnapshot>> ListProviders(
        IClientToolProviderRegistry registry,
        string? appProviderName,
        string? appKind,
        bool includeDisconnected)
        => TypedResults.Ok(registry.List(new ClientToolProviderQuery
        {
            AppProviderName = appProviderName,
            AppKind = appKind,
            IncludeDisconnected = includeDisconnected
        }));

    private static Results<Ok<ClientToolProviderSnapshot>, NotFound> GetProvider(
        IClientToolProviderRegistry registry,
        string clientRuntimeId)
        => registry.TryGet(clientRuntimeId, out var snapshot)
            ? TypedResults.Ok(snapshot)
            : TypedResults.NotFound();

    private static async Task<Results<Ok, BadRequest>> ConnectAsync(
        HttpContext context,
        IClientToolProviderRegistry registry,
        CancellationToken ct)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return TypedResults.BadRequest();

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        string? clientRuntimeId = null;
        string? connectionId = null;

        try
        {
            var helloText = await ReceiveTextMessageAsync(webSocket, ct).ConfigureAwait(false);
            if (helloText is null)
                return TypedResults.Ok();

            var messageType = ReadMessageType(helloText);
            if (!string.Equals(messageType, "provider.hello", StringComparison.Ordinal))
            {
                await SendJsonAsync(
                    webSocket,
                    new ClientToolProviderErrorMessage
                    {
                        Code = "hello_required",
                        Message = "The first provider message must be provider.hello."
                    },
                    ct).ConfigureAwait(false);
                return TypedResults.Ok();
            }

            var hello = JsonSerializer.Deserialize(
                helloText,
                HPDJsonContext.Default.ClientToolProviderHelloMessage);
            if (hello?.Identity is null)
            {
                await SendJsonAsync(
                    webSocket,
                    new ClientToolProviderErrorMessage
                    {
                        Code = "invalid_hello",
                        Message = "Provider hello message did not include a valid identity."
                    },
                    ct).ConfigureAwait(false);
                return TypedResults.Ok();
            }

            if (!string.Equals(hello.ProtocolVersion, "2", StringComparison.Ordinal))
            {
                await SendJsonAsync(
                    webSocket,
                    new ClientToolProviderErrorMessage
                    {
                        Code = "unsupported_protocol",
                        Message = $"Provider protocol '{hello.ProtocolVersion}' is unsupported. Expected '2'."
                    },
                    ct).ConfigureAwait(false);
                return TypedResults.Ok();
            }

            var connection = new WebSocketClientToolProviderConnection(webSocket);
            var registration = await registry.RegisterConnectionAsync(hello.Identity, connection, ct)
                .ConfigureAwait(false);
            clientRuntimeId = registration.ClientRuntimeId;
            connectionId = registration.ConnectionId;

            await SendJsonAsync(
                webSocket,
                new ClientToolProviderWelcomeMessage
                {
                    ClientRuntimeId = registration.ClientRuntimeId,
                    ConnectionId = registration.ConnectionId,
                    HeartbeatIntervalMs = (int)registration.HeartbeatInterval.TotalMilliseconds
                },
                ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextMessageAsync(webSocket, ct).ConfigureAwait(false);
                if (text is null)
                    break;

                await HandleProviderMessageAsync(
                    registry,
                    hello.Identity,
                    registration.ClientRuntimeId,
                    registration.ConnectionId,
                    text,
                    webSocket,
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(clientRuntimeId) &&
                !string.IsNullOrWhiteSpace(connectionId))
            {
                await registry.DisconnectAsync(clientRuntimeId, connectionId, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Provider connection closed.",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        return TypedResults.Ok();
    }

    private static async Task HandleProviderMessageAsync(
        IClientToolProviderRegistry registry,
        ClientToolProviderIdentity identity,
        string clientRuntimeId,
        string connectionId,
        string text,
        WebSocket webSocket,
        CancellationToken ct)
    {
        var messageType = ReadMessageType(text);
        switch (messageType)
        {
            case "provider.manifest":
                var manifestMessage = JsonSerializer.Deserialize(
                    text,
                    HPDJsonContext.Default.ClientToolProviderManifestMessage);
                if (manifestMessage is null)
                    return;

                await registry.UpdateManifestAsync(
                    clientRuntimeId,
                    connectionId,
                    new ClientToolProviderManifest
                    {
                        ProtocolVersion = manifestMessage.ProtocolVersion,
                        Identity = identity,
                        AppProvider = manifestMessage.AppProvider,
                        Context = manifestMessage.Context,
                        Readiness = manifestMessage.Readiness,
                        ClientToolHarnesses = manifestMessage.ClientToolHarnesses,
                        Metadata = manifestMessage.Metadata
                    },
                    ct).ConfigureAwait(false);
                break;

            case "provider.heartbeat":
                await registry.RecordHeartbeatAsync(clientRuntimeId, connectionId, ct)
                    .ConfigureAwait(false);
                break;

            case "provider.invokeOutcome":
                var outcome = JsonSerializer.Deserialize(
                    text,
                    HPDJsonContext.Default.ClientToolProviderInvokeOutcomeMessage);
                if (outcome is not null)
                    registry.TryResolveInvocationOutcome(clientRuntimeId, connectionId, outcome);
                break;

            case "provider.backgroundOperationOutcome":
                var backgroundOutcome = JsonSerializer.Deserialize(
                    text,
                    HPDJsonContext.Default.ClientToolProviderBackgroundOperationOutcomeMessage);
                if (backgroundOutcome is not null &&
                    !registry.TryResolveBackgroundOperationOutcome(clientRuntimeId, connectionId, backgroundOutcome))
                {
                    await SendJsonAsync(
                        webSocket,
                        new ClientToolProviderErrorMessage
                        {
                            Code = "background_operation_not_found",
                            Message = $"No active provider background operation '{backgroundOutcome.ClientOperationId}' was found for this binding."
                        },
                        ct).ConfigureAwait(false);
                }
                break;

            case "provider.release":
                var release = JsonSerializer.Deserialize(
                    text,
                    HPDJsonContext.Default.ClientToolProviderReleaseMessage);
                if (!string.IsNullOrWhiteSpace(release?.BindingId))
                {
                    await registry.ReleaseBindingAsync(release.BindingId, release.Reason, ct)
                        .ConfigureAwait(false);
                }
                break;

            default:
                await SendJsonAsync(
                    webSocket,
                    new ClientToolProviderErrorMessage
                    {
                        Code = "unsupported_message",
                        Message = $"Unsupported provider message type '{messageType ?? "<missing>"}'."
                    },
                    ct).ConfigureAwait(false);
                break;
        }
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("Client tool provider messages must be text frames.");

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? ReadMessageType(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("type", out var type)
            ? type.GetString()
            : null;
    }

    private static Task SendJsonAsync<T>(
        WebSocket webSocket,
        T value,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, typeof(T), HPDJsonContext.Default);
        var bytes = Encoding.UTF8.GetBytes(json);
        return webSocket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: ct);
    }

    private sealed class WebSocketClientToolProviderConnection : IClientToolProviderConnection
    {
        private readonly WebSocket _webSocket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public WebSocketClientToolProviderConnection(WebSocket webSocket)
        {
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        }

        public async ValueTask SendInvocationAsync(
            ClientToolProviderInvokeToolMessage message,
            CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendJsonAsync(_webSocket, message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
