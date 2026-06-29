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
    MCPServerConfig serverConfig,
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
                    GracePeriod = TimeSpan.FromMilliseconds(serverConfig.ShutdownTimeoutMs),
                },
            },
            Isolation = serverConfig.ProcessIsolation?.ToPolicy() ?? ProcessIsolationPolicy.Default,
            ObservationRetention = ObservationRetentionPolicy.EventsAndResult,
        };

        IProcessInvocationHandle handle = await processProvider
            .StartAsync(spec, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var transport = new McpProcessSessionTransport(serverConfig.Name, handle);
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

internal sealed class McpProcessSessionTransport : ITransport
{
    private readonly string _name;
    private readonly IProcessInvocationHandle _handle;
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateUnbounded<JsonRpcMessage>();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _readTask;

    public McpProcessSessionTransport(string name, IProcessInvocationHandle handle)
    {
        _name = name;
        _handle = handle;
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

        string json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
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
        var decoder = Encoding.UTF8.GetDecoder();
        var line = new StringBuilder();
        char[] chars = ArrayPool<char>.Shared.Rent(8192);

        try
        {
            await foreach (var chunk in _handle.ReadOutputAsync(cancellationToken).ConfigureAwait(false))
            {
                if (chunk.Stream is not ProcessOutputStream.Stdout)
                    continue;

                ReadOnlyMemory<byte> bytes = chunk.Bytes;
                while (!bytes.IsEmpty)
                {
                    decoder.Convert(
                        bytes.Span,
                        chars,
                        flush: false,
                        out int bytesUsed,
                        out int charsUsed,
                        out _);

                    AppendChars(line, chars.AsSpan(0, charsUsed));
                    bytes = bytes[bytesUsed..];
                }
            }

            FlushPendingLine(line);
            _messages.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _messages.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _messages.Writer.TryComplete(ex);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private void AppendChars(StringBuilder line, ReadOnlySpan<char> chars)
    {
        foreach (char ch in chars)
        {
            if (ch == '\n')
            {
                FlushPendingLine(line);
                continue;
            }

            if (ch != '\r')
                line.Append(ch);
        }
    }

    private void FlushPendingLine(StringBuilder line)
    {
        if (line.Length == 0)
            return;

        string json = line.ToString();
        line.Clear();

        JsonRpcMessage? message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);
        if (message is not null)
            _messages.Writer.TryWrite(message);
    }
}
