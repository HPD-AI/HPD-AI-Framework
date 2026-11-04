namespace HPD.Agent.ToolHarness.Coding.Tests;

using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

public sealed class LanguageServerEnvironmentProcessTests
{
    [Fact]
    public async Task Lsp_client_starts_through_the_selected_environment_process_provider()
    {
        var provider = new InitializeRespondingProcessProvider();
        var binding = new RuntimeProcessExecutionBinding
        {
            EnvironmentId = "test-environment",
            EnvironmentRevision = 7,
            ProcessProvider = provider,
            ExecutionTarget = new TargetHandle<ExecutionUnit>(
                new TargetRoute
                {
                    Kind = new TargetKind("execution-unit"),
                    Scope = new ResourceScope("test"),
                    ProviderId = new ProviderId(
                        "hpd.execution.test"),
                },
                TargetHandleLifetime.Lease,
                TargetHandleAuthority.Observe |
                TargetHandleAuthority.Control |
                TargetHandleAuthority.Invoke),
        };
        await using var client = new LanguageServerProtocolClient(
            "test-lsp",
            "/workspace",
            new LanguageServerLaunchDescriptor
            {
                FileName = "/guest/bin/test-lsp",
                Arguments = ["--stdio"],
            },
            new LanguageServerOptions(),
            processExecution: binding);

        await client.StartAsync(CancellationToken.None);

        client.IsRunning.Should().BeTrue();
        provider.StartedSpec.Should().NotBeNull();
        provider.StartedSpec!.Target.Should().Be(
            binding.ExecutionTarget);
        provider.StartedSpec.Command.FileName.Should().Be(
            "/guest/bin/test-lsp");
        provider.StartedSpec.Isolation.Should().Be(
            binding.Isolation);
    }

    private sealed class InitializeRespondingProcessProvider
        : IProcessProvider
    {
        public ProviderId ProviderId { get; } =
            new("hpd.execution.test");

        public ProcessInvocationSpec? StartedSpec { get; private set; }

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default)
        {
            StartedSpec = spec;
            return ValueTask.FromResult<IProcessInvocationHandle>(
                new InitializeRespondingHandle(spec));
        }

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class InitializeRespondingHandle(
        ProcessInvocationSpec spec)
        : IProcessInvocationHandle
    {
        private readonly Channel<ProcessOutputChunk> _output =
            Channel.CreateUnbounded<ProcessOutputChunk>();
        private readonly StringBuilder _input = new();
        private long _sequence;
        private bool _initializedResponseSent;
        private bool _shutdownResponseSent;

        public TargetHandle<ProcessInvocation> Handle { get; } =
            new(
                new TargetRoute
                {
                    Kind = new TargetKind("process"),
                    Scope = spec.Target.Route.Scope,
                    ProviderId = spec.Target.Route.ProviderId,
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Observe |
                TargetHandleAuthority.Control |
                TargetHandleAuthority.Invoke);

        public ResourceRef<ProcessInvocation>? Resource => null;
        public ProcessInvocationSpec Spec { get; } = spec;

        public ValueTask WriteStdinAsync(
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken = default)
        {
            _input.Append(Encoding.UTF8.GetString(bytes.Span));
            if (!_initializedResponseSent &&
                _input.ToString().Contains(
                    "\"method\":\"initialize\"",
                    StringComparison.Ordinal))
            {
                _initializedResponseSent = true;
                Publish(
                    """{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}""");
            }
            if (!_shutdownResponseSent &&
                _input.ToString().Contains(
                    "\"method\":\"shutdown\"",
                    StringComparison.Ordinal))
            {
                _shutdownResponseSent = true;
                Publish(
                    """{"jsonrpc":"2.0","id":2,"result":null}""");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseStdinAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SignalAsync(
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(
            ProcessStopRequest request,
            CancellationToken cancellationToken = default)
        {
            _output.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeTerminalAsync(
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ProcessInvocationResult> WaitAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ProcessOutputChunk>
            ReadOutputAsync(
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            await foreach (ProcessOutputChunk chunk in
                           _output.Reader.ReadAllAsync(
                               cancellationToken))
            {
                yield return chunk;
            }
        }

        public ValueTask DisposeAsync()
        {
            _output.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private void Publish(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes(
                $"Content-Length: {body.Length}\r\n\r\n");
            byte[] payload = [.. header, .. body];
            _output.Writer.TryWrite(new ProcessOutputChunk(
                Handle,
                ProcessOutputStream.Stdout,
                ++_sequence,
                DateTimeOffset.UtcNow,
                payload,
                ProcessOutputChunkFlags.None));
        }
    }
}
