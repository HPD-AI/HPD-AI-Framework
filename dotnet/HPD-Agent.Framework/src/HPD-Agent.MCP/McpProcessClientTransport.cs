using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HPD.Environment.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HPD.Agent.MCP;

internal sealed class McpProcessClientTransport(
    McpServerConfig serverConfig,
    IProcessProvider processProvider,
    IReadOnlyDictionary<string, string?> environment)
    : IClientTransport
{
    public string Name => serverConfig.Name;

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var spec = new ProcessInvocationSpec
        {
            Target = CreateTarget(serverConfig.Name),
            Role = ProcessRole.Sidecar,
            Command = new ProcessCommandSpec
            {
                FileName = serverConfig.Command!,
                Arguments = serverConfig.Arguments,
                WorkingDirectory = serverConfig.WorkingDirectory,
                Environment = environment,
            },
            Io = new ProcessIoSpec
            {
                StandardInput = new ProcessInputSpec { Kind = ProcessInputKind.Stream },
                StandardOutput = ProcessOutputSpec.CaptureAndStream,
                StandardError = ProcessOutputSpec.CaptureAndStream,
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                AllowBackground = true,
                Stop = new StopPolicy
                {
                    Kind = StopKind.GracefulThenKill,
                    GracePeriod = TimeSpan.FromSeconds(5),
                },
            },
            Isolation = serverConfig.ProcessIsolation?.ToPolicy() ?? ProcessIsolationPolicy.Default,
            ObservationRetention = ObservationRetentionPolicy.EventsAndResult,
        };

        IProcessInvocationHandle handle = await processProvider
            .StartAsync(spec, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var transport = new McpProcessSessionTransport(
            serverConfig.Name,
            handle,
            serverConfig.ProcessIsolation?.MaximumMessageBytes ?? 4 * 1024 * 1024);
        transport.Start(cancellationToken);
        return transport;
    }

    private static TargetHandle<ExecutionUnit> CreateTarget(string serverName) =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind("mcp.server"),
                Scope = new ResourceScope("mcp"),
                Segments =
                [
                    new TargetRouteSegment(TargetRouteSegmentKind.ExecutionUnit, serverName),
                ],
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read | TargetHandleAuthority.Write);
}

internal sealed class McpProcessTransportException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class McpProcessSessionTransport : ITransport
{
    private readonly string _name;
    private readonly IProcessInvocationHandle _handle;
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateUnbounded<JsonRpcMessage>();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly int _maximumMessageBytes;
    private Task? _readTask;

    public McpProcessSessionTransport(
        string name,
        IProcessInvocationHandle handle,
        int maximumMessageBytes = 4 * 1024 * 1024)
    {
        _name = name;
        _handle = handle;
        _maximumMessageBytes = maximumMessageBytes > 0
            ? maximumMessageBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
    }

    public string? SessionId { get; set; }

    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

    public void Start(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _readTask = Task.Run(() => ReadMessagesAsync(_disposeCts.Token), CancellationToken.None);
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeCts.IsCancellationRequested, this);

        string json = JsonSerializer.Serialize(
            message,
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)));
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _handle.WriteStdinAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposeCts.IsCancellationRequested)
            await _disposeCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _handle.CloseStdinAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await _handle.StopAsync(
                new ProcessStopRequest(StopKind.GracefulThenKill, $"MCP transport '{_name}' disposed"),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        if (_readTask is not null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await _handle.DisposeAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _disposeCts.Dispose();
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var line = new ArrayBufferWriter<byte>();

        try
        {
            await foreach (var chunk in _handle.ReadOutputAsync(cancellationToken).ConfigureAwait(false))
            {
                if (chunk.Stream is not ProcessOutputStream.Stdout)
                    continue;

                foreach (var value in chunk.Bytes.Span)
                {
                    if (value == (byte)'\n')
                    {
                        FlushPendingLine(line);
                        line.Clear();
                        continue;
                    }
                    if (line.WrittenCount >= _maximumMessageBytes)
                        throw new McpProcessTransportException(
                            $"MCP process '{_name}' emitted a message larger than {_maximumMessageBytes} bytes.");
                    line.GetSpan(1)[0] = value;
                    line.Advance(1);
                }
            }

            if (line.WrittenCount != 0)
                throw new McpProcessTransportException(
                    $"MCP process '{_name}' closed stdout in the middle of a JSON-RPC message.");
            _messages.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _messages.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _messages.Writer.TryComplete(ex is McpProcessTransportException
                ? ex
                : new McpProcessTransportException(
                    $"MCP process '{_name}' emitted malformed protocol output.", ex));
        }
    }

    private void FlushPendingLine(ArrayBufferWriter<byte> line)
    {
        var bytes = line.WrittenSpan;
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];
        if (bytes.IsEmpty)
            return;
        JsonRpcMessage? message = (JsonRpcMessage?)JsonSerializer.Deserialize(
            bytes,
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)));
        if (message is null)
            throw new JsonException("JSON-RPC payload resolved to null.");
        _messages.Writer.TryWrite(message);
    }
}
