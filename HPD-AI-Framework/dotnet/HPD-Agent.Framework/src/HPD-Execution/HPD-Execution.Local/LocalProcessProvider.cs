namespace HPD.Execution.Local;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;

public sealed class LocalProcessProviderModule : IProviderModule
{
    private readonly IProcessProvider _provider;

    public LocalProcessProviderModule()
        : this(new LocalProcessProvider())
    {
    }

    internal LocalProcessProviderModule(IProcessProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ProviderDescriptor Descriptor { get; } = new()
    {
        Id = LocalProcessProvider.LocalProviderId,
        DisplayName = "HPD Local Process Provider",
        ContractVersion = new SemanticVersion(1, 0, 0),
        ProviderVersion = new SemanticVersion(1, 0, 0),
        ContractKinds = ProviderContractKind.ProcessInvocation,
        TrustLevel = ProviderTrustLevel.BuiltIn,
        DefaultActivationScope = ProviderActivationScope.Runtime,
        ActivationModels =
        [
            new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None),
        ],
        HostPlatforms = [LocalProcessProvider.CurrentPlatform()],
    };

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProcessProvider(_provider);
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
    }
}

public sealed class LocalProcessProvider : IProcessProvider
{
    public static ProviderId LocalProviderId { get; } = new("hpd.execution.local.process");

    public ProviderId ProviderId => LocalProviderId;

    internal static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    public async ValueTask<IProcessInvocationHandle> StartAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var handle = new LocalProcessInvocationHandle(spec, output);
        await handle.StartAsync(cancellationToken).ConfigureAwait(false);
        return handle;
    }

    public async ValueTask<ProcessInvocationResult> RunAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        await using IProcessInvocationHandle handle = await StartAsync(spec, output, cancellationToken).ConfigureAwait(false);
        return await handle.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SignalAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessSignal signal,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Signal by target handle is not available for local ephemeral process handles.");

    public ValueTask ResizeTerminalAsync(
        TargetHandle<ProcessInvocation> process,
        TerminalSpec size,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Terminal resize by target handle is not available for local ephemeral process handles.");

    public ValueTask<ProcessInvocationResult> WaitAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Wait by target handle is not available for local ephemeral process handles.");

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        TargetHandle<ProcessInvocation> process,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}

public static class LocalProcessRegistrationExtensions
{
    public static ExecutionProviderRegistry RegisterLocalProcessProvider(this ExecutionProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(new LocalProcessProviderModule());
        return registry;
    }
}

internal sealed class LocalProcessInvocationHandle : IProcessInvocationHandle
{
    private readonly ProcessInvocationSpec _spec;
    private readonly IProcessOutputSink? _sink;
    private readonly Channel<ProcessOutputChunk> _output = Channel.CreateUnbounded<ProcessOutputChunk>();
    private readonly MemoryStream _stdout = new();
    private readonly MemoryStream _stderr = new();
    private readonly TaskCompletionSource<ProcessInvocationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _captureLock = new();
    private Process? _process;
    private DateTimeOffset _startedAt;
    private long _stdoutObserved;
    private long _stderrObserved;
    private long _stdoutCaptured;
    private long _stderrCaptured;
    private bool _stdoutTruncated;
    private bool _stderrTruncated;

    public LocalProcessInvocationHandle(ProcessInvocationSpec spec, IProcessOutputSink? sink)
    {
        _spec = spec;
        _sink = sink;
        Handle = new TargetHandle<ProcessInvocation>(
            new TargetRoute
            {
                Kind = new TargetKind("local.process"),
                Scope = new ResourceScope("local"),
                ProviderId = LocalProcessProvider.LocalProviderId,
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read,
            ProviderGeneration: 0);
    }

    public TargetHandle<ProcessInvocation> Handle { get; }

    public ResourceRef<ProcessInvocation>? Resource => null;

    public ProcessInvocationSpec Spec => _spec;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _spec.Command.FileName,
            UseShellExecute = false,
            RedirectStandardInput = _spec.Io.StandardInput.Kind is not ProcessInputKind.None,
            RedirectStandardOutput = _spec.Io.StandardOutput.Capture || _spec.Io.StandardOutput.Stream,
            RedirectStandardError = !_spec.Io.MergeStandardError && (_spec.Io.StandardError.Capture || _spec.Io.StandardError.Stream),
        };

        foreach (var argument in _spec.Command.Arguments)
            startInfo.ArgumentList.Add(argument);

        if (!string.IsNullOrWhiteSpace(_spec.Command.WorkingDirectory))
            startInfo.WorkingDirectory = _spec.Command.WorkingDirectory;

        foreach (var (key, value) in _spec.Command.Environment)
        {
            if (value is null)
                startInfo.Environment.Remove(key);
            else
                startInfo.Environment[key] = value;
        }

        if (_spec.Io.MergeStandardError)
            startInfo.RedirectStandardError = false;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        _process = process;
        _startedAt = DateTimeOffset.UtcNow;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Failed to start local process.");
        }
        catch (Exception ex)
        {
            _completion.TrySetResult(CreateFailedToStartResult(ex.Message));
            process.Dispose();
            throw;
        }

        _ = Task.Run(() => PumpProcessAsync(process, cancellationToken), CancellationToken.None);

        if (_spec.Io.StandardInput.Kind == ProcessInputKind.InlineBytes)
        {
            await process.StandardInput.BaseStream.WriteAsync(_spec.Io.StandardInput.InlineBytes, cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }
    }

    public async ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        Process process = RequireProcess();
        await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default)
    {
        RequireProcess().StandardInput.Close();
        return ValueTask.CompletedTask;
    }

    public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Local process signals are not implemented yet.");

    public async ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default)
    {
        Process process = RequireProcess();
        if (process.HasExited)
            return;

        if (request.Kind is StopKind.Kill)
        {
            process.Kill(entireProcessTree: _spec.Policy.StopProcessTree);
            return;
        }

        process.CloseMainWindow();
        TimeSpan grace = request.GracePeriod ?? _spec.Policy.Stop.GracePeriod;
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(grace, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (request.Kind is StopKind.GracefulThenKill)
                process.Kill(entireProcessTree: _spec.Policy.StopProcessTree);
        }
    }

    public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Local process terminal resize is not implemented yet.");

    public async ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) =>
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in _output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false } process)
            process.Kill(entireProcessTree: true);

        if (_process is not null)
        {
            _process.Dispose();
            _process = null;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task PumpProcessAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeoutCts = _spec.Policy.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts?.Token ?? CancellationToken.None);

        Task stdout = process.StartInfo.RedirectStandardOutput
            ? PumpStreamAsync(process.StandardOutput.BaseStream, ProcessOutputStream.Stdout, _spec.Io.StandardOutput, linkedCts.Token)
            : Task.CompletedTask;
        Task stderr = process.StartInfo.RedirectStandardError
            ? PumpStreamAsync(process.StandardError.BaseStream, ProcessOutputStream.Stderr, _spec.Io.StandardError, linkedCts.Token)
            : Task.CompletedTask;

        ProcessCompletionKind completionKind = ProcessCompletionKind.Completed;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            completionKind = process.ExitCode == 0 ? ProcessCompletionKind.Completed : ProcessCompletionKind.Exited;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            completionKind = ProcessCompletionKind.TimedOut;
            TryKill(process);
        }
        catch (OperationCanceledException)
        {
            completionKind = ProcessCompletionKind.Cancelled;
            if (_spec.Policy.StopOnRunCancellation)
                TryKill(process);
        }

        var outputDrainTimedOut = false;
        try
        {
            await Task.WhenAll(stdout, stderr).WaitAsync(_spec.Policy.OutputDrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            outputDrainTimedOut = true;
        }

        var exitedAt = DateTimeOffset.UtcNow;
        _output.Writer.TryComplete();
        _completion.TrySetResult(CreateResult(process, completionKind, exitedAt, outputDrainTimedOut));
    }

    private async Task PumpStreamAsync(
        Stream stream,
        ProcessOutputStream streamKind,
        ProcessOutputSpec spec,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        long sequence = 0;
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                byte[] bytes = buffer.AsSpan(0, read).ToArray();
                Capture(streamKind, bytes, spec.MaxCapturedBytes);

                if (spec.Stream)
                {
                    var chunk = new ProcessOutputChunk(
                        Handle,
                        streamKind,
                        sequence++,
                        DateTimeOffset.UtcNow,
                        bytes,
                        ProcessOutputChunkFlags.None);
                    await _output.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                    if (_sink is not null)
                        await _sink.OnOutputAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Capture(ProcessOutputStream stream, byte[] bytes, int? maxCapturedBytes)
    {
        lock (_captureLock)
        {
            MemoryStream target = stream is ProcessOutputStream.Stdout ? _stdout : _stderr;
            long observed = stream is ProcessOutputStream.Stdout
                ? _stdoutObserved += bytes.Length
                : _stderrObserved += bytes.Length;
            _ = observed;

            int max = maxCapturedBytes ?? int.MaxValue;
            long captured = stream is ProcessOutputStream.Stdout ? _stdoutCaptured : _stderrCaptured;
            int canWrite = (int)Math.Max(0, Math.Min(bytes.Length, max - captured));
            if (canWrite > 0)
            {
                target.Write(bytes, 0, canWrite);
                if (stream is ProcessOutputStream.Stdout)
                    _stdoutCaptured += canWrite;
                else
                    _stderrCaptured += canWrite;
            }

            if (canWrite < bytes.Length)
            {
                if (stream is ProcessOutputStream.Stdout)
                    _stdoutTruncated = true;
                else
                    _stderrTruncated = true;
            }
        }
    }

    private ProcessInvocationResult CreateFailedToStartResult(string message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ProcessInvocationResult
        {
            CompletionKind = ProcessCompletionKind.FailedToStart,
            StartedAt = _startedAt == default ? now : _startedAt,
            ExitedAt = now,
            Duration = TimeSpan.Zero,
            Output = CreateCapturedOutput(outputDrainTimedOut: false),
            Diagnostics =
            [
                new Condition(
                    "ProcessFailedToStart",
                    ConditionStatus.True,
                    "StartFailed",
                    message,
                    now,
                    new ResourceGeneration(0),
                    DiagnosticSeverity.Error),
            ],
        };
    }

    private ProcessInvocationResult CreateResult(
        Process process,
        ProcessCompletionKind completionKind,
        DateTimeOffset exitedAt,
        bool outputDrainTimedOut) =>
        new()
        {
            SystemProcessId = process.Id,
            ProviderProcessId = process.Id.ToString(),
            ExitCode = process.HasExited ? process.ExitCode : null,
            CompletionKind = completionKind,
            StartedAt = _startedAt,
            ExitedAt = exitedAt,
            Duration = exitedAt - _startedAt,
            Output = CreateCapturedOutput(outputDrainTimedOut),
        };

    private ProcessCapturedOutput CreateCapturedOutput(bool outputDrainTimedOut)
    {
        lock (_captureLock)
        {
            return new ProcessCapturedOutput
            {
                Stdout = ToStreamOutput(_stdout, _stdoutObserved, _stdoutCaptured, _stdoutTruncated),
                Stderr = ToStreamOutput(_stderr, _stderrObserved, _stderrCaptured, _stderrTruncated),
                MergedStandardError = _spec.Io.MergeStandardError,
                OutputDrainTimedOut = outputDrainTimedOut,
                OutputDrainTimeout = _spec.Policy.OutputDrainTimeout,
            };
        }
    }

    private static ProcessStreamOutput ToStreamOutput(
        MemoryStream stream,
        long observed,
        long captured,
        bool truncated) =>
        new()
        {
            CapturedBytes = stream.ToArray(),
            BytesObserved = observed,
            BytesCaptured = captured,
            BytesDiscarded = Math.Max(0, observed - captured),
            Truncated = truncated,
        };

    private Process RequireProcess() =>
        _process ?? throw new InvalidOperationException("The local process has not started.");

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
