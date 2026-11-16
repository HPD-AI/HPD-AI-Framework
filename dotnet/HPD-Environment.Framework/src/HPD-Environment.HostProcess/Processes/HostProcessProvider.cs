namespace HPD.Environment.HostProcess;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using HPD.Environment.HostProcess;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.HostProcess.Platforms.Windows;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>
/// Executes and enforces sandboxed processes directly on the current host.
/// </summary>
internal sealed class HostProcessProcessProvider : IProcessProvider
{
    private readonly HostProcessProviderState _state;
    private readonly ISandboxPlanner? _sandboxPlanner;
    private readonly ISandboxApplicator? _sandboxApplicator;
    private WindowsAppContainerProfile? _workspaceProfile;
    private IReadOnlyList<string>? _workspacePaths;

    internal void SetWindowsWorkspaces(WindowsAppContainerProfile profile, IReadOnlyList<string> paths)
    {
        _workspaceProfile = profile;
        _workspacePaths = paths;
    }

    public HostProcessProcessProvider(
        HostProcessProviderState state,
        ISandboxPlanner sandboxPlanner,
        ISandboxApplicator sandboxApplicator)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _sandboxPlanner = sandboxPlanner ?? throw new ArgumentNullException(nameof(sandboxPlanner));
        _sandboxApplicator = sandboxApplicator ?? throw new ArgumentNullException(nameof(sandboxApplicator));
    }

    public ProviderId ProviderId =>
        HostProcessProviderDescriptor.ProviderId;

    public bool SupportsProcessIsolation => _sandboxPlanner is not null && _sandboxApplicator is not null;

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
        cancellationToken.ThrowIfCancellationRequested();
        RequireReadyUnit(spec.Target);
        var (prepared, appContainerPlan) = await PrepareSandboxAsync(spec, cancellationToken).ConfigureAwait(false);
        var handle = new HostProcessInvocationHandle(prepared, output);
        if (OperatingSystem.IsWindows() && appContainerPlan is not null)
            await handle.StartAppContainerAsync(appContainerPlan, cancellationToken, _workspaceProfile, _workspacePaths).ConfigureAwait(false);
        else
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

    private async ValueTask<(ProcessInvocationSpec Spec, SandboxIsolationPlan? AppContainerPlan)> PrepareSandboxAsync(
        ProcessInvocationSpec spec,
        CancellationToken cancellationToken)
    {
        if (spec.Isolation.Mode is ProcessIsolationMode.Disabled)
            return (spec, null);

        if (_sandboxPlanner is null || _sandboxApplicator is null)
        {
            if (spec.Isolation.Mode is ProcessIsolationMode.Isolated)
                throw new InvalidOperationException(
                    "Process isolation was required, but the Host Process provider was not created with a sandbox planner and applicator.");

            return (spec, null);
        }

        PlatformSpec platform = CurrentPlatform();
        SandboxPlanEnvelope plan = await _sandboxPlanner.PlanAsync(
            spec,
            new SandboxExecutionContext
            {
                HostPlatform = platform,
                ExecutionPlatform = platform,
                EnforcementLocation = SandboxEnforcementLocation.Host,
                Scope = spec.Target.Route.Scope,
            },
            cancellationToken).ConfigureAwait(false);

        PreparedSandboxCommand command = await _sandboxApplicator.ApplyAsync(
            CommandInvocation.From(spec.Command.FileName, spec.Command.Arguments),
            plan,
            cancellationToken).ConfigureAwait(false);

        return (spec with
        {
            Command = spec.Command with
            {
                FileName = command.FileName,
                Arguments = command.ArgumentList,
                Environment = MergeEnvironment(spec.Command.Environment, command.Environment),
            },
            ProviderExtensions = spec.ProviderExtensions.Concat(plan.ProviderExtensions).ToArray(),
        }, command.UseWindowsAppContainer ? plan.Plan : null);
    }

    private static IReadOnlyDictionary<string, string?> MergeEnvironment(
        IReadOnlyDictionary<string, string?> invocation,
        IReadOnlyDictionary<string, string> sandbox)
    {
        if (sandbox.Count == 0)
            return invocation;

        var merged = new Dictionary<string, string?>(invocation, StringComparer.Ordinal);
        foreach (var (key, value) in sandbox)
            merged[key] = value;

        return merged;
    }

    private void RequireReadyUnit(TargetHandle<ExecutionUnit> unit)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup =
            HostProcessExecutionUnitProvider.Lookup(_state, unit);
        if (!lookup.Succeeded ||
            lookup.Entry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or
                ExecutionUnitPhase.Running))
        {
            throw new InvalidOperationException(
                lookup.Diagnostic?.Message ??
                "The Host Process execution unit is not ready.");
        }
    }

    public ValueTask SignalAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessSignal signal,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Signal by target handle is not available for ephemeral Host Process handles.");

    public ValueTask ResizeTerminalAsync(
        TargetHandle<ProcessInvocation> process,
        TerminalSpec size,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Terminal resize by target handle is not available for ephemeral Host Process handles.");

    public ValueTask<ProcessInvocationResult> WaitAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Wait by target handle is not available for ephemeral Host Process handles.");

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        TargetHandle<ProcessInvocation> process,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}

internal sealed class HostProcessInvocationHandle : IProcessInvocationHandle
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
    private WindowsRestrictedProcess? _restricted;
    private IDisposable? _appContainerProfile;
    private IDisposable? _appContainerWorkspace;
    private Task? _pump;
    private DateTimeOffset _startedAt;
    private long _stdoutObserved;
    private long _stderrObserved;
    private long _stdoutCaptured;
    private long _stderrCaptured;
    private bool _stdoutTruncated;
    private bool _stderrTruncated;

    public HostProcessInvocationHandle(
        ProcessInvocationSpec spec,
        IProcessOutputSink? sink)
    {
        _spec = spec;
        _sink = sink;
        Handle = new TargetHandle<ProcessInvocation>(
            new TargetRoute
            {
                Kind = new TargetKind("host-process.process"),
                Scope = spec.Target.Route.Scope,
                ProviderId = HostProcessProviderDescriptor.ProviderId,
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
                throw new InvalidOperationException(
                    "Failed to start Host Process.");
        }
        catch (Exception ex)
        {
            _completion.TrySetResult(CreateFailedToStartResult(ex.Message));
            process.Dispose();
            throw;
        }

        await StartPumpsAsync(process, cancellationToken).ConfigureAwait(false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal async ValueTask StartAppContainerAsync(SandboxIsolationPlan plan, CancellationToken cancellationToken,
        WindowsAppContainerProfile? sharedProfile = null, IReadOnlyList<string>? sharedPaths = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!plan.Interactive.AllowStdin && _spec.Io.StandardInput.Kind is not ProcessInputKind.None)
            throw new PlatformNotSupportedException("This Windows sandbox policy disallows standard input.");

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(plan.Environment.AllowedVariables, StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            string name = (string)entry.Key;
            if (!plan.Environment.StripUnlistedVariables || allowed.Contains(name))
                environment[name] = (string)entry.Value!;
        }
        foreach (var (name, value) in _spec.Command.Environment)
        {
            if (plan.Environment.StripUnlistedVariables && !allowed.Contains(name) &&
                !plan.Environment.InjectedVariables.ContainsKey(name))
                continue;
            if (value is null)
                environment.Remove(name);
            else
                environment[name] = value;
        }

        environment["SystemRoot"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        environment["LOCALAPPDATA"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        if (!_spec.Command.Environment.Any(entry =>
                entry.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase) && entry.Value is null))
            environment.TryAdd("PATH", System.Environment.SystemDirectory);
        if (!_spec.Command.Environment.Any(entry =>
                entry.Key.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) && entry.Value is null))
            environment.TryAdd("PATHEXT", ".COM;.EXE;.BAT;.CMD");
        try
        {
            var profile = sharedProfile ?? new WindowsAppContainerProfile();
            if (sharedProfile is null)
                _appContainerProfile = profile;
            if (sharedPaths is null && !string.IsNullOrWhiteSpace(_spec.Command.WorkingDirectory))
                throw new PlatformNotSupportedException("Bind the live workspace before specifying an AppContainer working directory.");

            var scratch = WindowsSandboxWorkspace.Create(profile.Sid);
            _appContainerWorkspace = scratch;
            string workspacePath = scratch.Path;
            if (sharedPaths is not null)
            {
                string requested = string.IsNullOrWhiteSpace(_spec.Command.WorkingDirectory)
                    ? sharedPaths[0]
                    : Path.GetFullPath(_spec.Command.WorkingDirectory, sharedPaths[0]);
                string root = sharedPaths.FirstOrDefault(path => WindowsSandboxWorkspace.ContainsDirectory(path, requested))
                    ?? throw new UnauthorizedAccessException("The working directory is outside the bound workspaces.");
                workspacePath = WindowsSandboxWorkspace.ResolveDirectory(root, requested);
            }
            environment["TEMP"] = scratch.Path;
            environment["TMP"] = scratch.Path;

            string executable = ResolveWindowsExecutable(_spec.Command.FileName, workspacePath, sharedPaths, environment);
            var arguments = _spec.Command.Arguments.ToArray();
            // Windows PowerShell can initialize its provider location at the drive root inside AppContainer.
            if (Path.GetFileName(executable) is var executableName &&
                (executableName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                 executableName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) &&
                arguments.Length >= 2 && arguments[^2].Equals("-Command", StringComparison.OrdinalIgnoreCase))
            {
                string literal = workspacePath.Replace("'", "''", StringComparison.Ordinal);
                arguments[^1] = $"New-PSDrive -Name HPDWorkspace -PSProvider FileSystem -Root '{literal}' -ErrorAction Stop | Out-Null; Set-Location HPDWorkspace: -ErrorAction Stop; {arguments[^1]}";
            }
            string commandLine = string.Join(" ", arguments.Prepend(executable).Select(QuoteWindowsArgument));
            cancellationToken.ThrowIfCancellationRequested();
            var child = WindowsRestrictedProcess.Start(null, executable, commandLine, workspacePath,
                environment, _spec.Io.MergeStandardError, profile);
            await AttachRestrictedProcessAsync(child, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static string ResolveWindowsExecutable(string name, string workingDirectory,
        IReadOnlyList<string>? workspaceRoots, IReadOnlyDictionary<string, string> environment)
    {
        if (Path.IsPathFullyQualified(name))
            return name;
        if (Path.IsPathRooted(name) || name.Contains(':'))
            throw new ArgumentException("Drive-relative and root-relative executable paths are ambiguous.", nameof(name));

        if (name.Contains('\\') || name.Contains('/'))
        {
            string candidate = Path.GetFullPath(name, workingDirectory);
            string root = workspaceRoots?.FirstOrDefault(path =>
                WindowsSandboxWorkspace.ContainsDirectory(path, candidate))
                ?? throw new UnauthorizedAccessException("The relative executable is outside the bound workspaces.");
            WindowsSandboxWorkspace.ResolveDirectory(root, Path.GetDirectoryName(candidate));
            string? resolved = FindExecutable(candidate);
            if (resolved is null)
                throw new FileNotFoundException("The relative executable was not found.", candidate);
            if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Relative executables must not be reparse points.");
            return resolved;
        }

        environment.TryGetValue("PATH", out string? pathValue);
        var directories = (pathValue ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Prepend(System.Environment.SystemDirectory);
        foreach (string directory in directories)
        {
            string path = directory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(path) && FindExecutable(Path.Combine(path, name)) is { } executable)
                return executable;
        }

        throw new FileNotFoundException("The executable was not found in the Windows system directory or sandbox PATH.", name);

        static string? FindExecutable(string candidate)
        {
            if (!Path.HasExtension(candidate))
                candidate += ".exe";
            return File.Exists(candidate) ? candidate : null;
        }
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return argument;

        var quoted = new System.Text.StringBuilder().Append('"');
        int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }

    // Takes ownership of a child prepared by the Windows launcher.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal async ValueTask AttachRestrictedProcessAsync(
        WindowsRestrictedProcess restricted, CancellationToken cancellationToken)
    {
        _restricted = restricted;
        _startedAt = DateTimeOffset.UtcNow;
        try
        {
            _process = Process.GetProcessById(restricted.ProcessId);
            await StartPumpsAsync(_process, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Stream? Stdin(Process process) => OperatingSystem.IsWindows() && _restricted is { } child
        ? child.StandardInput
        : process.StartInfo.RedirectStandardInput ? process.StandardInput.BaseStream : null;

    private async ValueTask StartPumpsAsync(Process process, CancellationToken cancellationToken)
    {
        _pump = Task.Run(async () =>
        {
            try
            {
                await PumpProcessAsync(process, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _output.Writer.TryComplete(ex);
                _completion.TrySetException(ex);
            }
        }, CancellationToken.None);
        if (_spec.Io.StandardInput.Kind == ProcessInputKind.InlineBytes)
        {
            Stream input = Stdin(process)!;
            await input.WriteAsync(_spec.Io.StandardInput.InlineBytes, cancellationToken).ConfigureAwait(false);
            input.Close();
        }
        else if (OperatingSystem.IsWindows() && _restricted is { } child &&
            _spec.Io.StandardInput.Kind == ProcessInputKind.None)
        {
            child.StandardInput.Close();
        }
    }

    public async ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        Stream input = Stdin(RequireProcess()) ?? throw new InvalidOperationException("Standard input is unavailable.");
        await input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default)
    {
        Stdin(RequireProcess())?.Close();
        return ValueTask.CompletedTask;
    }

    public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Host Process signals are not implemented yet.");

    public async ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default)
    {
        Process process = RequireProcess();
        if (OperatingSystem.IsWindows() && _restricted is { } child && request.Kind == StopKind.Kill)
        {
            child.StopProcessTree();
            return;
        }

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
            {
                if (OperatingSystem.IsWindows() && _restricted is { } restricted)
                    restricted.StopProcessTree();
                else
                    process.Kill(entireProcessTree: _spec.Policy.StopProcessTree);
            }
        }
    }

    public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Host Process terminal resize is not implemented yet.");

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
        if (OperatingSystem.IsWindows() && _restricted is { } child)
        {
            child.StopProcessTree();
            child.StandardInput.Dispose();
            try
            {
                if (_pump is not null)
                    await _pump.ConfigureAwait(false);
            }
            finally
            {
                child.Dispose();
                _process?.Dispose();
                _process = null;
                _restricted = null;
                DisposeAppContainer();
            }
        }
        else if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
        }

        if (_process is not null)
        {
            _process.Dispose();
            _process = null;
        }

        DisposeAppContainer();
    }

    private void DisposeAppContainer()
    {
        try
        {
            Interlocked.Exchange(ref _appContainerWorkspace, null)?.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref _appContainerProfile, null)?.Dispose();
        }
    }

    private async Task PumpProcessAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeoutCts = _spec.Policy.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts?.Token ?? CancellationToken.None);

        Stream? output, error;
        if (OperatingSystem.IsWindows() && _restricted is { } child)
        {
            output = child.StandardOutput;
            error = child.StandardError;
        }
        else
        {
            output = process.StartInfo.RedirectStandardOutput ? process.StandardOutput.BaseStream : null;
            error = process.StartInfo.RedirectStandardError ? process.StandardError.BaseStream : null;
        }

        Task stdout = output is not null
            ? PumpStreamAsync(output, ProcessOutputStream.Stdout, _spec.Io.StandardOutput, linkedCts.Token)
            : Task.CompletedTask;
        Task stderr = error is not null
            ? PumpStreamAsync(error, ProcessOutputStream.Stderr, _spec.Io.StandardError, linkedCts.Token)
            : Task.CompletedTask;

        ProcessCompletionKind completionKind = ProcessCompletionKind.Completed;
        try
        {
            if (OperatingSystem.IsWindows() && _restricted is { } restricted)
                await restricted.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            else
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            completionKind = ReadExitCode(process) == 0 ? ProcessCompletionKind.Completed : ProcessCompletionKind.Exited;
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

    private int? ReadExitCode(Process process) => OperatingSystem.IsWindows() && _restricted is { } child
        ? child.ReadExitCode()
        : process.HasExited ? process.ExitCode : null;

    private ProcessInvocationResult CreateResult(
        Process process,
        ProcessCompletionKind completionKind,
        DateTimeOffset exitedAt,
        bool outputDrainTimedOut) =>
        new()
        {
            SystemProcessId = process.Id,
            ProviderProcessId = process.Id.ToString(),
            ExitCode = ReadExitCode(process),
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
        _process ?? throw new InvalidOperationException(
            "The Host Process has not started.");

    private void TryKill(Process process)
    {
        if (OperatingSystem.IsWindows() && _restricted is { } child)
        {
            child.StopProcessTree();
            return;
        }
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
