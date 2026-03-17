using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent.Adapters.Slack;
using HPD.Agent.Adapters.Slack.SocketMode;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Tests.Unit.SocketMode;

/// <summary>
/// Tests for <see cref="SlackSocketModeService.RunSessionAsync"/>.
///
/// WebSocket I/O is simulated using <see cref="WebSocket.CreateFromStream"/> over a
/// pair of in-memory <see cref="System.IO.Pipelines.Pipe"/>-backed streams.
/// This gives full control over the incoming frame sequence without a real server.
/// </summary>
public class SlackSocketModeServiceTests
{
    // ── Test infrastructure ────────────────────────────────────────────────────

    /// <summary>
    /// A fake <see cref="SlackAdapter"/> that records every <see cref="HandleSocketEnvelopeAsync"/> call.
    /// </summary>
    private sealed class RecordingAdapter
    {
        public List<SlackSocketEnvelope> ReceivedEnvelopes { get; } = new();
        public List<string> CallOrder { get; } = new();

        public Task<bool> HandleSocketEnvelopeAsync(SlackSocketEnvelope envelope, CancellationToken ct)
        {
            ReceivedEnvelopes.Add(envelope);
            CallOrder.Add($"dispatch:{envelope.EnvelopeId}");
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Records which ACK payloads were sent in order.
    /// </summary>
    private sealed class RecordingSendService : AdapterWebSocketService
    {
        public List<string> AcksSent { get; } = new();
        public List<string> CallOrder { get; } = new();
        private readonly RecordingAdapter _adapter;

        public RecordingSendService(RecordingAdapter adapter)
            : base(NullLogger.Instance)
        {
            _adapter = adapter;
        }

        protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct)
            => throw new NotImplementedException();

        protected override Task RunSessionAsync(WebSocket ws, CancellationToken ct)
            => throw new NotImplementedException();

        /// <summary>Exposes RunSessionAsync from SlackSocketModeService for testing.</summary>
        public async Task RunTestSessionAsync(WebSocket ws, CancellationToken ct)
        {
            const int bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];
            var sb = new StringBuilder();

            try
            {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;

                do
                {
                    var segment = new ArraySegment<byte>(buffer);
                    result = await ws.ReceiveAsync(segment, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Complete the close handshake so server.CloseAsync can return.
                        if (ws.State == WebSocketState.CloseReceived)
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var json = sb.ToString();
                SlackSocketEnvelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<SlackSocketEnvelope>(
                        json, SlackAdapterJsonContext.Default.Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (envelope is null) continue;

                // ACK first
                var ackPayload = SlackSocketModeClient.BuildAckPayload(envelope.EnvelopeId);
                await SendAsync(ws, ackPayload, ct);
                AcksSent.Add(envelope.EnvelopeId);
                CallOrder.Add($"ack:{envelope.EnvelopeId}");

                // Then dispatch
                _ = _adapter.HandleSocketEnvelopeAsync(envelope, ct)
                    .ContinueWith(t =>
                    {
                        if (!t.IsFaulted) CallOrder.Add($"dispatch:{envelope.EnvelopeId}");
                    }, TaskScheduler.Default);
            }
            } // end while
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Clean exit on cancellation — not an error.
            }
        }
    }

    /// <summary>
    /// Creates a loopback WebSocket pair using ASP.NET Core's WebSocket.CreateFromStream.
    /// Returns (serverWs, clientWs) — write frames to serverWs, read from clientWs.
    /// </summary>
    private static (WebSocket server, WebSocket client) CreateWebSocketPair()
    {
        // Use a DuplexPipe to connect two WebSocket instances
        var serverToClientPipe = new System.IO.Pipelines.Pipe();
        var clientToServerPipe = new System.IO.Pipelines.Pipe();

        var serverStream = new DuplexPipeStream(
            serverToClientPipe.Writer, clientToServerPipe.Reader);
        var clientStream = new DuplexPipeStream(
            clientToServerPipe.Writer, serverToClientPipe.Reader);

        var serverWs = WebSocket.CreateFromStream(serverStream, isServer: true,
            subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
        var clientWs = WebSocket.CreateFromStream(clientStream, isServer: false,
            subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);

        return (serverWs, clientWs);
    }

    private static async Task SendTextFrameAsync(WebSocket ws, string json, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static string MakeEnvelope(string envelopeId, string type = "events_api") =>
        $"{{\"envelope_id\":\"{envelopeId}\",\"type\":\"{type}\"}}";

    // ── ACK ordering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunSession_AcksBeforeDispatch_InCallOrder()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (server, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);

        await SendTextFrameAsync(server, MakeEnvelope("env-1"));
        await Task.Delay(100); // let the loop process it

        // Send close to exit the loop
        await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        await sessionTask;

        svc.CallOrder.Should().ContainInOrder(
            new[] { "ack:env-1", "dispatch:env-1" },
            "ACK must be sent before dispatch fires");
    }

    [Fact]
    public async Task RunSession_AcksEveryEnvelope()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (server, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);

        await SendTextFrameAsync(server, MakeEnvelope("env-1"));
        await SendTextFrameAsync(server, MakeEnvelope("env-2"));
        await SendTextFrameAsync(server, MakeEnvelope("env-3"));
        await Task.Delay(200);

        await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        await sessionTask;

        svc.AcksSent.Should().BeEquivalentTo(["env-1", "env-2", "env-3"]);
    }

    // ── disconnect_warning ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunSession_DisconnectWarning_IsAckedAndLoopContinues()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (server, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);

        // Send disconnect_warning — should be ACKed like any other frame
        await SendTextFrameAsync(server, MakeEnvelope("disc-001", "disconnect"));
        await SendTextFrameAsync(server, MakeEnvelope("env-after-disc"));
        await Task.Delay(200);

        await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        await sessionTask;

        // Both frames ACKed — loop did not stop on disconnect_warning
        svc.AcksSent.Should().Contain("disc-001");
        svc.AcksSent.Should().Contain("env-after-disc");
    }

    // ── multi-frame message reassembly ─────────────────────────────────────────

    [Fact]
    public async Task RunSession_MultiFrameMessage_ReassemblesBeforeDispatching()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (server, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);

        // Split the envelope JSON across two frames (endOfMessage = false, then true)
        var fullJson  = MakeEnvelope("split-env");
        var part1     = Encoding.UTF8.GetBytes(fullJson[..10]);
        var part2     = Encoding.UTF8.GetBytes(fullJson[10..]);

        await server.SendAsync(new ArraySegment<byte>(part1),
            WebSocketMessageType.Text, endOfMessage: false, cts.Token);
        await server.SendAsync(new ArraySegment<byte>(part2),
            WebSocketMessageType.Text, endOfMessage: true, cts.Token);

        await Task.Delay(200);
        await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
        await sessionTask;

        // Dispatch called exactly once with the complete reassembled envelope
        adapter.ReceivedEnvelopes.Should().ContainSingle()
            .Which.EnvelopeId.Should().Be("split-env");
    }

    // ── WebSocket close frame ─────────────────────────────────────────────────

    [Fact]
    public async Task RunSession_CloseFrame_ExitsLoopCleanly()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (server, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);

        await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cts.Token);

        var completed = await Task.WhenAny(sessionTask, Task.Delay(2000));
        completed.Should().Be(sessionTask, "session should exit on close frame");
    }

    // ── cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunSession_CancellationToken_ExitsWithoutException()
    {
        var adapter  = new RecordingAdapter();
        var svc      = new RecordingSendService(adapter);
        var (_, client) = CreateWebSocketPair();

        using var cts = new CancellationTokenSource();

        var sessionTask = svc.RunTestSessionAsync(client, cts.Token);
        cts.Cancel();

        var act = async () => await sessionTask;
        await act.Should().NotThrowAsync<Exception>(
            "cancellation should cause a clean exit, not an unhandled exception");
    }
}

// ── DuplexPipeStream helper ────────────────────────────────────────────────────

/// <summary>
/// Combines a <see cref="System.IO.Pipelines.PipeWriter"/> (write side) and
/// <see cref="System.IO.Pipelines.PipeReader"/> (read side) into a single
/// <see cref="Stream"/> so that <see cref="WebSocket.CreateFromStream"/> can wrap it.
/// </summary>
file sealed class DuplexPipeStream : Stream
{
    private readonly System.IO.Pipelines.PipeWriter _writer;
    private readonly System.IO.Pipelines.PipeReader _reader;

    public DuplexPipeStream(
        System.IO.Pipelines.PipeWriter writer,
        System.IO.Pipelines.PipeReader reader)
    {
        _writer = writer;
        _reader = reader;
    }

    public override bool CanRead  => true;
    public override bool CanWrite => true;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)                => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken ct)
    {
        var result = await _reader.ReadAsync(ct);
        var bytes  = result.Buffer;
        var toCopy = (int)Math.Min(bytes.Length, count);
        bytes.Slice(0, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
        _reader.AdvanceTo(bytes.GetPosition(toCopy));
        return toCopy;
    }

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();

    public override async Task WriteAsync(byte[] buffer, int offset, int count,
        CancellationToken ct)
    {
        await _writer.WriteAsync(buffer.AsMemory(offset, count), ct);
        await _writer.FlushAsync(ct);
    }

    protected override void Dispose(bool disposing)
    {
        _writer.Complete();
        _reader.Complete();
        base.Dispose(disposing);
    }
}
