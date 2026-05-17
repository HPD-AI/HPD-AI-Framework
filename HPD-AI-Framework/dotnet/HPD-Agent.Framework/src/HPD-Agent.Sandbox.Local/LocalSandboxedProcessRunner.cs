using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Events;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.State;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

internal sealed class LocalSandboxedProcessRunner : ISandboxedProcessRunner, IAsyncDisposable
{
    private readonly object _managerLock = new();
    private readonly object _processLock = new();
    private readonly Dictionary<string, SandboxManager> _managers = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, ActiveSandboxedProcess> _activeProcesses = new();
    private readonly SandboxConfig _globalConfig;
    private readonly ISandboxPolicyResolver _policyResolver;
    private readonly ILogger? _logger;
    private readonly Func<CommandInvocation, SandboxConfig, CancellationToken, Task<SandboxedCommand>>? _wrapCommandAsync;
    private readonly SandboxViolationStore? _violationStore;
    private readonly Action<AgentEvent>? _eventSink;
    private bool _disposed;

    public LocalSandboxedProcessRunner(
        SandboxConfig globalConfig,
        ISandboxPolicyResolver? policyResolver = null,
        ILogger? logger = null,
        Action<AgentEvent>? eventSink = null)
    {
        _globalConfig = globalConfig ?? throw new ArgumentNullException(nameof(globalConfig));
        _policyResolver = policyResolver ?? new DefaultSandboxPolicyResolver();
        _logger = logger;
        _eventSink = eventSink;
    }

    internal LocalSandboxedProcessRunner(
        SandboxConfig globalConfig,
        Func<CommandInvocation, SandboxConfig, CancellationToken, Task<SandboxedCommand>> wrapCommandAsync,
        ISandboxPolicyResolver? policyResolver = null,
        ILogger? logger = null,
        Action<AgentEvent>? eventSink = null,
        SandboxViolationStore? violationStore = null)
        : this(globalConfig, policyResolver, logger, eventSink)
    {
        _wrapCommandAsync = wrapCommandAsync ?? throw new ArgumentNullException(nameof(wrapCommandAsync));
        _violationStore = violationStore;
    }

    internal int ActiveProcessCount
    {
        get
        {
            lock (_processLock)
                return _activeProcesses.Count;
        }
    }

    public async Task<ISandboxedProcessHandle> StartAsync(
        SandboxedProcessCommand command,
        SandboxConfigOverride? configOverride = null,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(command);
        command.Validate();

        options ??= new SandboxedProcessOptions();
        if (options.RequirePty)
            throw new NotSupportedException("PTY execution is not implemented yet.");

        var effectiveConfig = _policyResolver.Resolve(_globalConfig, callOverride: configOverride);
        var manager = _wrapCommandAsync is null ? GetManager(effectiveConfig) : null;
        var violationStore = manager?.ViolationStore ?? _violationStore;
        var violationBaseline = violationStore?.TotalCount ?? 0;
        var invocation = ToInvocation(command);
        var wrapped = await WrapCommandAsync(
            manager,
            invocation,
            effectiveConfig,
            cancellationToken).ConfigureAwait(false);

        var processId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var process = CreateProcess(wrapped, command, options);

        try
        {
            Emit(CreateProcessEvent<SandboxProcessStartingEvent>(
                processId,
                command,
                invocation,
                effectiveConfig,
                startedAt,
                stopwatch.Elapsed));

            _logger?.LogDebug("Starting sandboxed process: {FileName}", wrapped.FileName);
            if (!process.Start())
                throw new InvalidOperationException("Failed to start sandboxed process.");

            var handle = new LocalSandboxedProcessHandle(
                this,
                processId,
                process,
                command,
                invocation,
                effectiveConfig,
                options,
                violationStore,
                violationBaseline,
                startedAt,
                stopwatch,
                cancellationToken);

            TrackProcess(processId, process, command, invocation, effectiveConfig, startedAt, handle);
            Emit(CreateProcessEvent<SandboxProcessStartedEvent>(
                processId,
                command,
                invocation,
                effectiveConfig,
                startedAt,
                stopwatch.Elapsed) with
            {
                SystemProcessId = process.Id
            });

            handle.Start();
            return handle;
        }
        catch (Exception ex)
        {
            Emit(CreateProcessEvent<SandboxProcessFailedEvent>(
                processId,
                command,
                invocation,
                effectiveConfig,
                startedAt,
                stopwatch.Elapsed) with
            {
                Message = ex.Message
            });
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await KillActiveProcessesAsync().ConfigureAwait(false);

        SandboxManager[] managers;
        lock (_managerLock)
        {
            managers = _managers.Values.ToArray();
            _managers.Clear();
        }

        foreach (var manager in managers)
            await manager.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<SandboxedCommand> WrapCommandAsync(
        SandboxManager? manager,
        CommandInvocation invocation,
        SandboxConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        if (_wrapCommandAsync is not null)
            return await _wrapCommandAsync(invocation, effectiveConfig, cancellationToken).ConfigureAwait(false);

        manager ??= GetManager(effectiveConfig);
        return await manager.WrapCommandAsync(
            invocation.FileName,
            invocation.ArgumentList,
            effectiveConfig,
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<SandboxedProcessViolation> GetProcessViolations(
        SandboxViolationStore? violationStore,
        int violationBaseline)
    {
        if (violationStore is null)
            return Array.Empty<SandboxedProcessViolation>();

        return violationStore
            .GetSinceTotalCount(violationBaseline)
            .Select(ToProcessViolation)
            .ToArray();
    }

    private static SandboxedProcessViolation ToProcessViolation(SandboxViolation violation)
    {
        return new SandboxedProcessViolation(
            violation.Type.ToString(),
            violation.Message,
            violation.Path);
    }

    private SandboxManager GetManager(SandboxConfig config)
    {
        var key = CreateInfrastructureKey(config);
        lock (_managerLock)
        {
            if (_managers.TryGetValue(key, out var manager))
                return manager;

            manager = new SandboxManager(_logger);
            _managers[key] = manager;
            return manager;
        }
    }

    private void TrackProcess(
        Guid processId,
        Process process,
        SandboxedProcessCommand command,
        CommandInvocation invocation,
        SandboxConfig effectiveConfig,
        DateTimeOffset startedAt,
        ISandboxedProcessHandle handle)
    {
        lock (_processLock)
        {
            if (_disposed)
            {
                KillProcess(process, new SandboxedProcessOptions());
                throw new ObjectDisposedException(nameof(LocalSandboxedProcessRunner));
            }

            _activeProcesses[processId] = new ActiveSandboxedProcess(
                processId,
                process,
                command,
                invocation,
                effectiveConfig,
                startedAt,
                handle);
        }
    }

    private void UntrackProcess(Guid processId)
    {
        lock (_processLock)
            _activeProcesses.Remove(processId);
    }

    private async Task KillActiveProcessesAsync()
    {
        ActiveSandboxedProcess[] processes;
        lock (_processLock)
        {
            processes = _activeProcesses.Values.ToArray();
            _activeProcesses.Clear();
        }

        foreach (var active in processes)
        {
            await active.Handle.StopAsync(SandboxedProcessStopReason.Disposed).ConfigureAwait(false);
            Emit(CreateProcessEvent<SandboxProcessKilledEvent>(
                active.ProcessId,
                active.Command,
                active.Invocation,
                active.EffectiveConfig,
                active.StartedAt,
                DateTimeOffset.UtcNow - active.StartedAt) with
            {
                Reason = "runner disposed"
            });
        }
    }

    private void Emit(AgentEvent evt) => _eventSink?.Invoke(evt);

    private static TEvent CreateProcessEvent<TEvent>(
        Guid processId,
        SandboxedProcessCommand command,
        CommandInvocation invocation,
        SandboxConfig effectiveConfig,
        DateTimeOffset startedAt,
        TimeSpan duration)
        where TEvent : SandboxProcessEvent, new()
    {
        return new TEvent
        {
            ProcessId = processId.ToString("N"),
            CommandKind = "argv",
            FileName = invocation.FileName,
            WorkingDirectory = command.WorkingDirectory,
            NetworkMode = effectiveConfig.NetworkMode,
            Platform = PlatformDetector.Current.ToString(),
            Timestamp = startedAt,
            Duration = duration
        };
    }

    private static string CreateInfrastructureKey(SandboxConfig config)
    {
        var text = string.Join('\n',
            config.NetworkMode.ToString(),
            Join(config.AllowedDomains),
            Join(config.DeniedDomains),
            config.ParentProxy?.HttpProxy ?? "",
            config.ParentProxy?.HttpsProxy ?? "",
            config.ParentProxy?.NoProxy ?? "",
            config.TlsTermination?.CaCertificatePath ?? "",
            config.TlsTermination?.CaPrivateKeyPath ?? "",
            config.TlsTermination?.LeafCertificateCacheDirectory ?? "",
            config.TlsTermination?.InjectTrustEnvironmentVariables.ToString() ?? "",
            config.MitmProxy?.UnixSocketPath ?? "",
            config.ExternalHttpProxyPort?.ToString() ?? "",
            config.ExternalSocksProxyPort?.ToString() ?? "",
            config.RequestFilter is null ? "" : config.RequestFilter.GetHashCode().ToString());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Join(IEnumerable<string>? values) =>
        values is null ? "" : string.Join('\u001f', values);

    private static CommandInvocation ToInvocation(SandboxedProcessCommand command)
    {
        return CommandInvocation.From(command.FileName, command.Arguments);
    }

    private static Process CreateProcess(
        SandboxedCommand wrapped,
        SandboxedProcessCommand original,
        SandboxedProcessOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = wrapped.FileName,
            UseShellExecute = false,
            RedirectStandardInput = options.StandardInput is not null,
            RedirectStandardOutput = options.CaptureStandardOutput,
            RedirectStandardError = options.CaptureStandardError
        };

        if (!string.IsNullOrWhiteSpace(original.WorkingDirectory))
            startInfo.WorkingDirectory = original.WorkingDirectory;

        foreach (var argument in wrapped.ArgumentList)
            startInfo.ArgumentList.Add(argument);

        if (wrapped.Environment is not null)
        {
            foreach (var (key, value) in wrapped.Environment)
                startInfo.Environment[key] = value;
        }

        foreach (var (key, value) in original.Environment)
        {
            if (value is null)
                startInfo.Environment.Remove(key);
            else
                startInfo.Environment[key] = value;
        }

        return new Process { StartInfo = startInfo };
    }

    private sealed class LocalSandboxedProcessHandle : ISandboxedProcessHandle
    {
        private readonly LocalSandboxedProcessRunner _owner;
        private readonly Guid _processGuid;
        private readonly Process _process;
        private readonly CommandInvocation _invocation;
        private readonly SandboxConfig _effectiveConfig;
        private readonly SandboxViolationStore? _violationStore;
        private readonly int _violationBaseline;
        private readonly DateTimeOffset _startedAt;
        private readonly Stopwatch _stopwatch;
        private readonly CancellationTokenSource? _timeoutCts;
        private readonly CancellationTokenSource _linkedCts;
        private readonly IEventCoordinator _events;
        private readonly EventCoordinator? _ownedEvents;
        private readonly TaskCompletionSource<SandboxedProcessResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopRequested;
        private SandboxedProcessStopReason? _stopReason;
        private Task? _runTask;

        public LocalSandboxedProcessHandle(
            LocalSandboxedProcessRunner owner,
            Guid processGuid,
            Process process,
            SandboxedProcessCommand command,
            CommandInvocation invocation,
            SandboxConfig effectiveConfig,
            SandboxedProcessOptions options,
            SandboxViolationStore? violationStore,
            int violationBaseline,
            DateTimeOffset startedAt,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _processGuid = processGuid;
            _process = process;
            Command = command;
            _invocation = invocation;
            _effectiveConfig = effectiveConfig;
            Options = options;
            _violationStore = violationStore;
            _violationBaseline = violationBaseline;
            _startedAt = startedAt;
            _stopwatch = stopwatch;
            if (options.EventCoordinator is { } eventCoordinator)
            {
                _events = eventCoordinator;
            }
            else
            {
                _ownedEvents = new EventCoordinator();
                _events = _ownedEvents;
            }
            _timeoutCts = options.Timeout is { } timeout
                ? new CancellationTokenSource(timeout)
                : null;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _timeoutCts?.Token ?? CancellationToken.None);
        }

        public string ProcessId => _processGuid.ToString("N");

        public int? SystemProcessId
        {
            get
            {
                try
                {
                    return _process.Id;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public SandboxedProcessCommand Command { get; }

        public SandboxedProcessOptions Options { get; }

        public IEventCoordinator Events => _events;

        public Task<SandboxedProcessResult> Completion => _completion.Task;

        public void Start()
        {
            Publish(new SandboxedProcessStartedEvent
            {
                ProcessId = ProcessId,
                SystemProcessId = SystemProcessId,
                FileName = _invocation.FileName
            });
            _runTask = RunToCompletionAsync();
        }

        public Task StopAsync(
            SandboxedProcessStopReason reason = SandboxedProcessStopReason.Requested,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestStop(reason);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            RequestStop(SandboxedProcessStopReason.Disposed);

            try
            {
                await Completion.ConfigureAwait(false);
            }
            catch
            {
                // Completion converts process failures into result objects where possible.
            }
        }

        private async Task RunToCompletionAsync()
        {
            var output = new ProcessOutputCapture(Options.MaxCapturedBytesPerStream);
            var error = new ProcessOutputCapture(Options.MaxCapturedBytesPerStream);
            using var outputReadCts = new CancellationTokenSource();
            Task outputTask = Task.CompletedTask;
            Task errorTask = Task.CompletedTask;
            var outputDrainTimedOut = false;

            try
            {
                outputTask = Options.CaptureStandardOutput
                    ? ReadToCaptureAsync(
                        _process.StandardOutput.BaseStream,
                        SandboxedProcessStream.Stdout,
                        output,
                        outputReadCts.Token)
                    : Task.CompletedTask;
                errorTask = Options.CaptureStandardError
                    ? ReadToCaptureAsync(
                        _process.StandardError.BaseStream,
                        SandboxedProcessStream.Stderr,
                        error,
                        outputReadCts.Token)
                    : Task.CompletedTask;

                if (Options.StandardInput is not null)
                {
                    await _process.StandardInput.WriteAsync(
                        Options.StandardInput.AsMemory(),
                        _linkedCts.Token).ConfigureAwait(false);
                }

                if (_process.StartInfo.RedirectStandardInput)
                    _process.StandardInput.Close();

                await _process.WaitForExitAsync(_linkedCts.Token).ConfigureAwait(false);
                outputDrainTimedOut = await DrainOutputAsync(
                    outputTask,
                    errorTask,
                    outputReadCts).ConfigureAwait(false);

                var completionKind = GetCompletionKindAfterExit();
                var capturedOutput = CreateCapturedOutput(output, error, outputDrainTimedOut);

                if (completionKind == SandboxedProcessCompletionKind.Completed)
                {
                    _owner.Emit(CreateProcessEvent<SandboxProcessCompletedEvent>(
                        _processGuid,
                        Command,
                        _invocation,
                        _effectiveConfig,
                        _startedAt,
                        _stopwatch.Elapsed) with
                    {
                        ExitCode = _process.ExitCode
                    });
                }

                Complete(CreateResult(
                    GetExitCode(),
                    completionKind,
                    capturedOutput));
            }
            catch (OperationCanceledException) when (_timeoutCts?.IsCancellationRequested == true)
            {
                RequestStop(SandboxedProcessStopReason.Timeout);
                outputDrainTimedOut = await DrainOutputAsync(
                    outputTask,
                    errorTask,
                    outputReadCts).ConfigureAwait(false);
                var capturedOutput = CreateCapturedOutput(output, error, outputDrainTimedOut);
                _owner.Emit(CreateProcessEvent<SandboxProcessTimedOutEvent>(
                    _processGuid,
                    Command,
                    _invocation,
                    _effectiveConfig,
                    _startedAt,
                    _stopwatch.Elapsed) with
                {
                    Timeout = Options.Timeout!.Value
                });
                Complete(CreateResult(-1, SandboxedProcessCompletionKind.TimedOut, capturedOutput));
            }
            catch (OperationCanceledException)
                when (_stopReason == SandboxedProcessStopReason.Cancelled || _linkedCts.IsCancellationRequested)
            {
                RequestStop(SandboxedProcessStopReason.Cancelled);
                outputDrainTimedOut = await DrainOutputAsync(
                    outputTask,
                    errorTask,
                    outputReadCts).ConfigureAwait(false);
                var capturedOutput = CreateCapturedOutput(output, error, outputDrainTimedOut);
                _owner.Emit(CreateProcessEvent<SandboxProcessCancelledEvent>(
                    _processGuid,
                    Command,
                    _invocation,
                    _effectiveConfig,
                    _startedAt,
                    _stopwatch.Elapsed));
                Complete(CreateResult(-1, SandboxedProcessCompletionKind.Cancelled, capturedOutput));
            }
            catch (Exception ex)
            {
                outputDrainTimedOut = await DrainOutputAsync(
                    outputTask,
                    errorTask,
                    outputReadCts).ConfigureAwait(false);
                var capturedOutput = CreateCapturedOutput(output, error, outputDrainTimedOut);
                _owner.Emit(CreateProcessEvent<SandboxProcessFailedEvent>(
                    _processGuid,
                    Command,
                    _invocation,
                    _effectiveConfig,
                    _startedAt,
                    _stopwatch.Elapsed) with
                {
                    Message = ex.Message
                });
                Complete(CreateResult(-1, SandboxedProcessCompletionKind.Faulted, capturedOutput));
            }
            finally
            {
                _stopwatch.Stop();
                _owner.UntrackProcess(_processGuid);
                _ownedEvents?.Dispose();
                _linkedCts.Dispose();
                _timeoutCts?.Dispose();
                _process.Dispose();
            }
        }

        private void RequestStop(SandboxedProcessStopReason reason)
        {
            if (Interlocked.CompareExchange(ref _stopRequested, 1, 0) == 0)
                _stopReason = reason;

            KillProcess(_process, Options);
        }

        private SandboxedProcessCompletionKind GetCompletionKindAfterExit()
        {
            return _stopReason switch
            {
                SandboxedProcessStopReason.Requested => SandboxedProcessCompletionKind.Stopped,
                SandboxedProcessStopReason.Disposed => SandboxedProcessCompletionKind.Killed,
                SandboxedProcessStopReason.RuntimeStopping => SandboxedProcessCompletionKind.Killed,
                SandboxedProcessStopReason.Cancelled => SandboxedProcessCompletionKind.Cancelled,
                SandboxedProcessStopReason.Timeout => SandboxedProcessCompletionKind.TimedOut,
                _ => SandboxedProcessCompletionKind.Completed
            };
        }

        private int? GetExitCode()
        {
            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private SandboxedProcessResult CreateResult(
            int? exitCode,
            SandboxedProcessCompletionKind completionKind,
            SandboxedProcessCapturedOutput output)
        {
            var violations = GetProcessViolations(_violationStore, _violationBaseline);
            Publish(new SandboxedProcessExitedEvent
            {
                ProcessId = ProcessId,
                ExitCode = exitCode,
                CompletionKind = completionKind,
                Duration = _stopwatch.Elapsed,
                Output = output,
                Violations = violations
            });

            return new SandboxedProcessResult
            {
                ProcessId = ProcessId,
                SystemProcessId = SystemProcessId,
                ExitCode = exitCode,
                CompletionKind = completionKind,
                Output = output,
                Violations = violations
            };
        }

        private void Complete(SandboxedProcessResult result)
        {
            _completion.TrySetResult(result);
        }

        private async Task ReadToCaptureAsync(
            Stream stream,
            SandboxedProcessStream streamKind,
            ProcessOutputCapture capture,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return;

                var chunk = buffer.AsMemory(0, read).ToArray();
                Publish(new SandboxedProcessOutputEvent
                {
                    ProcessId = ProcessId,
                    Stream = streamKind,
                    Bytes = chunk
                });

                capture.Append(chunk.AsSpan());
            }
        }

        private async Task<bool> DrainOutputAsync(
            Task outputTask,
            Task errorTask,
            CancellationTokenSource outputReadCts)
        {
            var drainTask = Task.WhenAll(outputTask, errorTask);
            if (drainTask.IsCompleted)
            {
                await ObserveOutputReaderCompletionAsync(drainTask).ConfigureAwait(false);
                return false;
            }

            var timeoutTask = Task.Delay(Options.OutputDrainTimeout);
            if (await Task.WhenAny(drainTask, timeoutTask).ConfigureAwait(false) == drainTask)
            {
                await ObserveOutputReaderCompletionAsync(drainTask).ConfigureAwait(false);
                return false;
            }

            outputReadCts.Cancel();
            return true;
        }

        private static async Task ObserveOutputReaderCompletionAsync(Task drainTask)
        {
            try
            {
                await drainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        }

        private SandboxedProcessCapturedOutput CreateCapturedOutput(
            ProcessOutputCapture stdout,
            ProcessOutputCapture stderr,
            bool outputDrainTimedOut)
        {
            if (!Options.MergeStandardError)
            {
                return new SandboxedProcessCapturedOutput
                {
                    Stdout = stdout.ToStreamOutput(),
                    Stderr = stderr.ToStreamOutput(),
                    MergedStandardError = false,
                    OutputDrainTimedOut = outputDrainTimedOut,
                    OutputDrainTimeout = Options.OutputDrainTimeout
                };
            }

            var merged = ProcessOutputCapture.Merge(
                Options.MaxCapturedBytesPerStream,
                stdout,
                stderr);

            return new SandboxedProcessCapturedOutput
            {
                Stdout = merged.ToStreamOutput(),
                Stderr = new ProcessOutputCapture(Options.MaxCapturedBytesPerStream).ToStreamOutput(),
                MergedStandardError = true,
                OutputDrainTimedOut = outputDrainTimedOut,
                OutputDrainTimeout = Options.OutputDrainTimeout
            };
        }

        private void Publish(SandboxedProcessRuntimeEvent evt)
        {
            _events.Emit(evt);
        }

        private sealed class ProcessOutputCapture
        {
            private readonly int? _maxCapturedBytes;
            private readonly MemoryStream _captured = new();

            public ProcessOutputCapture(int? maxCapturedBytes)
            {
                _maxCapturedBytes = maxCapturedBytes;
            }

            public long BytesObserved { get; private set; }

            public long BytesCaptured => _captured.Length;

            public long BytesDiscarded => BytesObserved - BytesCaptured;

            public bool Truncated => BytesDiscarded > 0;

            public void Append(ReadOnlySpan<byte> bytes)
            {
                BytesObserved += bytes.Length;

                if (_maxCapturedBytes is null)
                {
                    _captured.Write(bytes);
                    return;
                }

                var remaining = _maxCapturedBytes.Value - _captured.Length;
                if (remaining <= 0)
                    return;

                _captured.Write(bytes[..(int)Math.Min(bytes.Length, remaining)]);
            }

            public SandboxedProcessStreamOutput ToStreamOutput()
            {
                var capturedBytes = _captured.ToArray();
                return new SandboxedProcessStreamOutput
                {
                    CapturedBytes = capturedBytes,
                    Text = Encoding.UTF8.GetString(capturedBytes),
                    BytesObserved = BytesObserved,
                    BytesCaptured = capturedBytes.Length,
                    BytesDiscarded = BytesDiscarded,
                    Truncated = Truncated
                };
            }

            public static ProcessOutputCapture Merge(
                int? maxCapturedBytes,
                ProcessOutputCapture stdout,
                ProcessOutputCapture stderr)
            {
                var merged = new ProcessOutputCapture(maxCapturedBytes)
                {
                    BytesObserved = stdout.BytesObserved + stderr.BytesObserved
                };

                var stdoutBytes = stdout._captured.ToArray();
                var stderrBytes = stderr._captured.ToArray();
                merged.AppendCapturedBytes(stdoutBytes);
                merged.AppendCapturedBytes(stderrBytes);
                return merged;
            }

            private void AppendCapturedBytes(ReadOnlySpan<byte> bytes)
            {
                if (_maxCapturedBytes is null)
                {
                    _captured.Write(bytes);
                    return;
                }

                var remaining = _maxCapturedBytes.Value - _captured.Length;
                if (remaining <= 0)
                    return;

                _captured.Write(bytes[..(int)Math.Min(bytes.Length, remaining)]);
            }
        }
    }

    private static void KillProcess(Process process, SandboxedProcessOptions options)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(options.KillProcessTreeOnCancel);
        }
        catch (InvalidOperationException)
        {
            // Process exited between HasExited and Kill.
        }
    }

    private sealed record ActiveSandboxedProcess(
        Guid ProcessId,
        Process Process,
        SandboxedProcessCommand Command,
        CommandInvocation Invocation,
        SandboxConfig EffectiveConfig,
        DateTimeOffset StartedAt,
        ISandboxedProcessHandle Handle);
}
