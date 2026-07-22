using System.Runtime.CompilerServices;
using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugProtocolTransportTests
{
    [Fact]
    public async Task In_memory_transport_preserves_partial_reads_writes_and_exit()
    {
        await using var transport = new InMemoryDebugProtocolTransport();
        await transport.FeedProtocolAsync("abcdef"u8.ToArray());
        var first = new byte[2];
        var second = new byte[4];

        (await transport.ReadProtocolAsync(first)).Should().Be(2);
        (await transport.ReadProtocolAsync(second)).Should().Be(4);
        await transport.WriteProtocolAsync("request"u8.ToArray());
        transport.Complete(new(ProcessCompletionKind.Exited, 7));

        first.Should().Equal("ab"u8.ToArray());
        second.Should().Equal("cdef"u8.ToArray());
        (await CollectAsync(transport.ReadWrittenAsync())).Should().ContainSingle().Which.Should().Equal("request"u8.ToArray());
        (await transport.WaitForExitAsync()).ExitCode.Should().Be(7);
    }

    [Fact]
    public async Task Environment_transport_enumerates_handle_once_and_separates_stdout_and_stderr()
    {
        var handle = new FakeInvocationHandle([
            Chunk(ProcessOutputStream.Stdout, "protocol"u8.ToArray(), 1),
            Chunk(ProcessOutputStream.Stderr, "diagnostic"u8.ToArray(), 2)
        ]);
        await using var transport = new DebugEnvironmentProcessTransport(handle);
        var protocol = new byte[8];

        (await transport.ReadProtocolAsync(protocol)).Should().Be(8);
        var diagnostics = await CollectAsync(transport.ReadDiagnosticsAsync());
        var exit = await transport.WaitForExitAsync();

        protocol.Should().Equal("protocol"u8.ToArray());
        diagnostics.Should().ContainSingle();
        Encoding.UTF8.GetString(diagnostics[0].Bytes.Span).Should().Be("diagnostic");
        handle.EnumerationCount.Should().Be(1);
        exit.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
    }

    [Fact]
    public async Task Borrowed_chunks_are_copied_before_provider_reuses_the_buffer()
    {
        var borrowed = "original"u8.ToArray();
        var handle = new FakeInvocationHandle([Chunk(ProcessOutputStream.Stdout, borrowed, 1, ProcessOutputChunkFlags.BorrowedBuffer)],
            afterYield: () => borrowed.AsSpan().Fill((byte)'x'));
        await using var transport = new DebugEnvironmentProcessTransport(handle);
        var received = new byte[8];

        await transport.ReadProtocolAsync(received);
        await transport.WaitForExitAsync();

        Encoding.UTF8.GetString(received).Should().Be("original");
        Encoding.UTF8.GetString(borrowed).Should().Be("xxxxxxxx");
    }

    [Fact]
    public async Task Protocol_overflow_or_truncation_faults_while_diagnostics_drop_without_blocking()
    {
        var overflowHandle = new FakeInvocationHandle([
            Chunk(ProcessOutputStream.Stdout, new byte[9], 1)
        ]);
        await using var overflow = new DebugEnvironmentProcessTransport(overflowHandle, new()
        {
            MaxBufferedProtocolBytes = 8,
            ProtocolChannelCapacity = 1,
            DiagnosticChannelCapacity = 1,
            MaxDiagnosticBytes = 8
        });

        (await overflow.WaitForExitAsync()).SafeReasonCode.Should().Be("TRANSPORT_PUMP_FAILED");

        var diagnosticsHandle = new FakeInvocationHandle([
            Chunk(ProcessOutputStream.Stderr, "12345678"u8.ToArray(), 1),
            Chunk(ProcessOutputStream.Stderr, "drop"u8.ToArray(), 2),
            Chunk(ProcessOutputStream.Stdout, "ok"u8.ToArray(), 3)
        ]);
        await using var diagnosticsTransport = new DebugEnvironmentProcessTransport(diagnosticsHandle, new()
        {
            MaxBufferedProtocolBytes = 32,
            ProtocolChannelCapacity = 1,
            DiagnosticChannelCapacity = 1,
            MaxDiagnosticBytes = 8
        });
        var protocol = new byte[2];
        await diagnosticsTransport.ReadProtocolAsync(protocol);
        var diagnostics = await CollectAsync(diagnosticsTransport.ReadDiagnosticsAsync());

        protocol.Should().Equal("ok"u8.ToArray());
        diagnostics.Last().DroppedChunks.Should().Be(1);
        diagnostics.Last().DroppedBytes.Should().Be(4);
    }

    private static ProcessOutputChunk Chunk(
        ProcessOutputStream stream,
        byte[] bytes,
        long sequence,
        ProcessOutputChunkFlags flags = ProcessOutputChunkFlags.None) => new(
            Handle(), stream, sequence, DateTimeOffset.UtcNow, bytes, flags);

    private static TargetHandle<ProcessInvocation> Handle() => new(
        new TargetRoute { Kind = new("test.process"), Scope = new("test") },
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Control | TargetHandleAuthority.Observe);

    private static TargetHandle<ExecutionUnit> ExecutionHandle() => new(
        new TargetRoute { Kind = new("test.execution"), Scope = new("test") },
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Control | TargetHandleAuthority.Observe);

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var values = new List<T>();
        await foreach (var value in source)
            values.Add(value);
        return values;
    }

    private sealed class FakeInvocationHandle(
        IReadOnlyList<ProcessOutputChunk> chunks,
        Action? afterYield = null) : IProcessInvocationHandle
    {
        public int EnumerationCount { get; private set; }
        public TargetHandle<ProcessInvocation> Handle { get; } = DebugProtocolTransportTests.Handle();
        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = new()
        {
            Target = DebugProtocolTransportTests.ExecutionHandle(),
            Command = new() { FileName = "fixture" }
        };
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProcessInvocationResult
        {
            CompletionKind = ProcessCompletionKind.Completed,
            Output = new()
            {
                Stdout = new(),
                Stderr = new(),
                OutputDrainTimeout = TimeSpan.Zero
            }
        });

        public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationCount++;
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
                afterYield?.Invoke();
                await Task.Yield();
            }
        }
    }
}
