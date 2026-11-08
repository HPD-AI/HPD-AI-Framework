using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Client.Tests;

public sealed class HpdAgentClientToolProviderWebSocketTests
{
    [Fact]
    public async Task Connect_HelloManifestAndInvokeOutcomeRoundTrip()
    {
        var socket = new ScriptedWebSocket(
            """
            {"type":"provider.welcome","clientRuntimeId":"runtime-one",
             "connectionId":"connection-one","heartbeatIntervalMs":60000}
            """,
            """
            {"type":"provider.invoke","protocolVersion":"2",
             "clientRuntimeId":"runtime-one","connectionId":"connection-one",
             "bindingId":"binding-one","invocationId":"invocation-one",
             "requestId":"request-one","toolName":"sample.echo",
             "visibleToolName":"echo","callId":"call-one",
             "arguments":{"value":"hello"},"resolvedInvocationMode":"Synchronous"}
            """);
        await using var provider = new HpdAgentClientToolProviderClient(
            new HpdAgentClientToolProviderOptions(
                new Uri("wss://example.test/client-tool-providers/connect"))
            {
                Identity = new HpdAgentClientToolProviderIdentity(
                    "sample-provider",
                    "sample-app"),
                AppProvider = new HpdAgentClientAppProvider("sample"),
                ClientToolHarnesses = [Json("""{"name":"sample","tools":[]}""")],
                WebSocketFactory = () => socket
            });
        provider.OnInvoke((invocation, _) =>
        {
            var value = invocation.Arguments.GetProperty("value").GetString();
            return ValueTask.FromResult(HpdAgentClientToolProviderOutcome.Completed(
                [HpdAgentClientToolResult.TextContent($"echo:{value}")]));
        });

        var welcome = await provider.ConnectAsync();
        await socket.WaitForMessageAsync("provider.invokeOutcome");

        Assert.Equal("runtime-one", welcome.ClientRuntimeId);
        var messages = socket.SentMessages;
        Assert.Contains(messages, static message =>
            message.Contains("\"type\":\"provider.hello\"", StringComparison.Ordinal));
        Assert.Contains(messages, static message =>
            message.Contains("\"type\":\"provider.manifest\"", StringComparison.Ordinal));
        var outcome = Assert.Single(messages, static message =>
            message.Contains("\"type\":\"provider.invokeOutcome\"", StringComparison.Ordinal));
        Assert.Contains("\"outcome\":\"Completed\"", outcome, StringComparison.Ordinal);
        Assert.Contains("echo:hello", outcome, StringComparison.Ordinal);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class ScriptedWebSocket(params string[] messages) : WebSocket
    {
        private readonly Queue<string> _messages = new(messages);
        private readonly List<string> _sent = [];
        private readonly SemaphoreSlim _sentChanged = new(0);
        private WebSocketState _state = WebSocketState.Open;

        public IReadOnlyList<string> SentMessages
        {
            get
            {
                lock (_sent)
                    return _sent.ToArray();
            }
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public async Task WaitForMessageAsync(string type)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                if (SentMessages.Any(message =>
                    message.Contains($"\"type\":\"{type}\"", StringComparison.Ordinal)))
                {
                    return;
                }
                await _sentChanged.WaitAsync(timeout.Token);
            }
        }

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            ReceiveCoreAsync(buffer, cancellationToken);

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken) =>
            ReceiveValueCoreAsync(buffer, cancellationToken);

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Record(buffer.AsSpan());
            return Task.CompletedTask;
        }

        public override ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Record(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private async Task<WebSocketReceiveResult> ReceiveCoreAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var bytes = await NextAsync(cancellationToken);
            bytes.CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(
                bytes.Length,
                WebSocketMessageType.Text,
                endOfMessage: true);
        }

        private async ValueTask<ValueWebSocketReceiveResult> ReceiveValueCoreAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            var bytes = await NextAsync(cancellationToken);
            bytes.CopyTo(buffer.Span);
            return new ValueWebSocketReceiveResult(
                bytes.Length,
                WebSocketMessageType.Text,
                endOfMessage: true);
        }

        private async Task<byte[]> NextAsync(CancellationToken cancellationToken)
        {
            if (_messages.TryDequeue(out var message))
                return Encoding.UTF8.GetBytes(message);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }

        private void Record(ReadOnlySpan<byte> bytes)
        {
            lock (_sent)
                _sent.Add(Encoding.UTF8.GetString(bytes));
            _sentChanged.Release();
        }
    }
}
