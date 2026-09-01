using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HPD.Agent.MCP;
using HPD.Environment.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpProcessTransportTests
{
    [Fact]
    public async Task IsolatedStdio_ConnectsThroughModernDiscoveryAndPreservesIsolationPolicy()
    {
        var provider = new FakeProcessProvider();
        var options = new McpOptions { ProcessProvider = provider };
        await using var runtime = new McpRuntime(NullLogger.Instance, options);

        var tools = await runtime.LoadToolsFromManifestContentAsync("""
            {"servers":[{"name":"isolated","transport":"stdio","command":"server","processIsolation":{"enabled":true,"allowNetwork":false}}]}
            """);

        Assert.Empty(tools);
        Assert.NotNull(provider.StartedSpec);
        Assert.Equal(ProcessIsolationMode.Isolated, provider.StartedSpec!.Isolation.Mode);
        Assert.Equal(NetworkEgressMode.Blocked, provider.StartedSpec.Isolation.Network.Mode);
        Assert.Equal(["server/discover", "tools/list"], provider.Handle.Methods);
    }

    [Fact]
    public async Task ReadsNewlineFramingAcrossChunksAndIgnoresStderr()
    {
        var handle = new FakeProcessHandle();
        await using var transport = new McpProcessSessionTransport("isolated", handle);
        transport.Start(default);

        handle.Publish(ProcessOutputStream.Stderr, "diagnostic\n");
        handle.Publish(ProcessOutputStream.Stdout, "{\"jsonrpc\":\"2.0\",\"id\":1,\"res");
        handle.Publish(ProcessOutputStream.Stdout, "ult\":{}}\r\n");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var message = await transport.MessageReader.ReadAsync(timeout.Token);

        Assert.IsType<JsonRpcResponse>(message);
    }

    [Fact]
    public async Task FramingHandlesSplitUtf8MultipleMessagesCrLfAndEmptyLines()
    {
        var handle = new FakeProcessHandle();
        await using var transport = new McpProcessSessionTransport("isolated", handle);
        transport.Start(default);
        var payload = Encoding.UTF8.GetBytes(
            "\n{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"text\":\"é\"}}\r\n" +
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}\n");
        var split = Array.IndexOf(payload, (byte)0xC3) + 1;

        handle.Publish(ProcessOutputStream.Stdout, payload.AsMemory(0, split));
        handle.Publish(ProcessOutputStream.Stdout, payload.AsMemory(split));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.IsType<JsonRpcResponse>(await transport.MessageReader.ReadAsync(timeout.Token));
        Assert.IsType<JsonRpcResponse>(await transport.MessageReader.ReadAsync(timeout.Token));
    }

    [Theory]
    [InlineData("not-json\n", 1024)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1}", 1024)]
    [InlineData("12345", 4)]
    public async Task MalformedOversizedOrMidMessageClosureCompletesWithTypedFailure(
        string output,
        int maximumBytes)
    {
        var handle = new FakeProcessHandle();
        await using var transport = new McpProcessSessionTransport("isolated", handle, maximumBytes);
        transport.Start(default);
        handle.Publish(ProcessOutputStream.Stdout, output);
        handle.CompleteOutput();

        await Assert.ThrowsAsync<McpProcessTransportException>(() =>
            transport.MessageReader.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task WritesExactlyOneNewlineDelimitedJsonRpcMessage()
    {
        var handle = new FakeProcessHandle();
        await using var transport = new McpProcessSessionTransport("isolated", handle);
        transport.Start(default);
        var message = Parse("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");

        await transport.SendMessageAsync(message);

        var bytes = Assert.Single(handle.Writes);
        var text = Encoding.UTF8.GetString(bytes.Span);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(text);
        Assert.Equal("ping", document.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ConcurrentSendsRemainIndividuallyFramedAndCancellationPropagates()
    {
        var handle = new FakeProcessHandle();
        await using var transport = new McpProcessSessionTransport("isolated", handle);
        transport.Start(default);
        await Task.WhenAll(
            transport.SendMessageAsync(Parse("""{"jsonrpc":"2.0","id":1,"method":"one"}""")),
            transport.SendMessageAsync(Parse("""{"jsonrpc":"2.0","id":2,"method":"two"}""")));

        Assert.Equal(2, handle.Writes.Count);
        Assert.All(handle.Writes, bytes => Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes.Span)));

        handle.BlockWrites = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.SendMessageAsync(
                Parse("""{"jsonrpc":"2.0","id":3,"method":"cancel"}"""),
                cancellation.Token));
    }

    [Fact]
    public async Task DisposalCancelsReaderClosesInputStopsAndDisposesHandle()
    {
        var handle = new FakeProcessHandle();
        var transport = new McpProcessSessionTransport("isolated", handle);
        transport.Start(default);

        await transport.DisposeAsync();

        Assert.True(handle.InputClosed);
        Assert.Equal(1, handle.StopCount);
        Assert.Equal(1, handle.DisposeCount);
        await transport.MessageReader.Completion;
    }

    private static JsonRpcMessage Parse(string json) =>
        (JsonRpcMessage?)JsonSerializer.Deserialize(
            json,
            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)))
        ?? throw new InvalidOperationException("Invalid test JSON-RPC message.");

    private sealed class FakeProcessHandle(bool respond = false) : IProcessInvocationHandle
    {
        private readonly Channel<ProcessOutputChunk> _output = Channel.CreateUnbounded<ProcessOutputChunk>();
        private long _sequence;

        public TargetHandle<ProcessInvocation> Handle => default;
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec => null!;
        public List<ReadOnlyMemory<byte>> Writes { get; } = [];
        public bool InputClosed { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public List<string> Methods { get; } = [];
        public bool BlockWrites { get; set; }

        public void Publish(ProcessOutputStream stream, string text) =>
            Publish(stream, Encoding.UTF8.GetBytes(text));

        public void Publish(ProcessOutputStream stream, ReadOnlyMemory<byte> bytes) =>
            _output.Writer.TryWrite(new ProcessOutputChunk(
                default,
                stream,
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                bytes,
                ProcessOutputChunkFlags.None));

        public void CompleteOutput() => _output.Writer.TryComplete();

        public async ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BlockWrites)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Writes.Add(bytes.ToArray());
            if (respond)
                Respond(Encoding.UTF8.GetString(bytes.Span));
        }

        private void Respond(string requestJson)
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            var id = root.GetProperty("id").GetRawText();
            var result = method switch
            {
                "server/discover" => """{"resultType":"complete","supportedVersions":["2026-07-28"],"capabilities":{"tools":{}},"_meta":{"io.modelcontextprotocol/serverInfo":{"name":"isolated","version":"1"}},"ttlMs":0,"cacheScope":"private"}""",
                "tools/list" => """{"resultType":"complete","tools":[],"ttlMs":0,"cacheScope":"private"}""",
                _ => throw new InvalidOperationException($"Unexpected MCP method '{method}'.")
            };
            Publish(ProcessOutputStream.Stdout,
                $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{result}}}\n");
        }

        public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default)
        {
            InputClosed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in _output.Reader.ReadAllAsync(cancellationToken))
                yield return chunk;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _output.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public ProviderId ProviderId { get; } = new("test-isolated");
        public FakeProcessHandle Handle { get; } = new(respond: true);
        public ProcessInvocationSpec? StartedSpec { get; private set; }

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            StartedSpec = spec;
            return ValueTask.FromResult<IProcessInvocationHandle>(Handle);
        }

        public ValueTask<ProcessInvocationResult> RunAsync(ProcessInvocationSpec spec, IProcessOutputSink? output = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SignalAsync(TargetHandle<ProcessInvocation> process, ProcessSignal signal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ResizeTerminalAsync(TargetHandle<ProcessInvocation> process, TerminalSpec size, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ProcessInvocationResult> WaitAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(TargetHandle<ProcessInvocation> process, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
