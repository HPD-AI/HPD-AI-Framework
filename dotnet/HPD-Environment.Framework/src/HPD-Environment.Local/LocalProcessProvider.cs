namespace HPD.Environment.Local;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalProcessProvider(LocalProviderState state)
    : IProcessProvider, IRetainedProcessProvider
{
    private static readonly HashSet<string> AllowedComposeOperations =
        new(StringComparer.Ordinal)
        {
            "hpdos-compose-stage",
            "hpdos-compose-images",
            "hpdos-compose-image-inspection",
            "hpdos-compose-stop",
            "hpdos-compose-recover-stopped",
            "hpdos-compose-absent",
            "hpdos-compose-remove",
            "hpdos-compose-inspect",
        };
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("process-invocation"),
        TargetRouteSegmentKind.ProcessInvocation,
        TargetHandleLifetime.LiveCapability,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Invoke,
        new SchemaId("hpd.execution.local.process.handle.v1"));

    private readonly ConcurrentDictionary<string, LocalProcessOperation>
        _processes = new(StringComparer.Ordinal);
    private long _processSequence;

    public ProviderId ProviderId =>
        LocalEnvironmentProviderDescriptor.ProviderId;

    public async ValueTask<IProcessInvocationHandle> StartAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        RequireReadyUnit(spec.Target);
        LocalResolvedCommand command = ResolveCommand(spec);
        string id =
            $"local-process-{Interlocked.Increment(ref _processSequence)}";
        ResourceMetadata<ProcessInvocation> metadata = new()
        {
            Id = new ResourceId<ProcessInvocation>(id),
            Kind = new ResourceKind("ProcessInvocation"),
            Scope = spec.Target.Route.Scope,
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var prepared = new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Reconciling,
            ProcessPhase = ProcessInvocationPhase.Prepared,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = startedAt,
            StartedAt = startedAt,
            IoState = ProcessIoState.Open,
        };
        ProviderResourceEntry<
            ProcessInvocation,
            ProcessInvocationSpec,
            ProcessInvocationStatus> entry =
            state.Ledger.Upsert(metadata, spec, prepared, Shape);

        var operation = new LocalProcessOperation(
            state,
            entry.Resource,
            entry.TargetHandle,
            spec,
            command,
            output,
            status => StoreStatus(entry.Resource, spec, status));
        if (!_processes.TryAdd(id, operation))
            throw new InvalidOperationException(
                "The Local process identity was already allocated.");
        try
        {
            await operation.StartAsync(cancellationToken)
                .ConfigureAwait(false);
            StoreStatus(entry.Resource, spec, operation.Status);
        }
        catch
        {
            bool cleaned =
                await operation.CleanupFailedStartAsync()
                    .ConfigureAwait(false);
            if (cleaned)
            {
                _processes.TryRemove(id, out _);
                state.Ledger.Remove<
                    ProcessInvocation,
                    ProcessInvocationSpec,
                    ProcessInvocationStatus>(entry.Resource);
                await operation.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
        return operation;
    }

    public async ValueTask<ProcessInvocationResult> RunAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        await using IProcessInvocationHandle handle =
            await StartAsync(spec, output, cancellationToken)
                .ConfigureAwait(false);
        return await handle.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask SignalAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessSignal signal,
        CancellationToken cancellationToken = default) =>
        Require(process).SignalAsync(signal, cancellationToken);

    public ValueTask ResizeTerminalAsync(
        TargetHandle<ProcessInvocation> process,
        TerminalSpec size,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(
            new NotSupportedException(
                "The Local provider does not expose a host terminal."));

    public ValueTask<ProcessInvocationResult> WaitAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default) =>
        Require(process).WaitAsync(cancellationToken);

    public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default) =>
        Require(process).ReadOutputAsync(cancellationToken);

    public ValueTask<ProcessInvocationStatus> GetStatusAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Require(process).Status);
    }

    public ValueTask StopAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessStopRequest request,
        CancellationToken cancellationToken = default) =>
        Require(process).StopAsync(request, cancellationToken);

    public async ValueTask ReleaseAsync(
        ResourceRef<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_processes.TryRemove(process.Id.Value, out var operation))
            await operation.DisposeAsync().ConfigureAwait(false);
        state.Ledger.Remove<
            ProcessInvocation,
            ProcessInvocationSpec,
            ProcessInvocationStatus>(process);
    }

    private void RequireReadyUnit(TargetHandle<ExecutionUnit> unit)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(unit);
        if (!lookup.Succeeded ||
            lookup.Entry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or ExecutionUnitPhase.Running))
        {
            throw new InvalidOperationException(
                lookup.Diagnostic?.Message ??
                "The Local execution unit is not ready.");
        }
    }

    private LocalResolvedCommand ResolveCommand(ProcessInvocationSpec spec)
    {
        string? workingDirectory =
            ResolveWorkingDirectory(
                spec.Command.WorkingDirectory);
        bool engineOperation =
            spec.Command.FileName is "/usr/bin/docker" or
            "/hpd/container-run" ||
            spec.Command.Arguments.Any(argument =>
                argument.Contains(
                    "/run/hpd/engine/docker.sock",
                    StringComparison.Ordinal) ||
                argument.Contains(
                    "/usr/bin/docker",
                    StringComparison.Ordinal));
        if (engineOperation)
            ValidateEngineAuthority(spec);

        string docker = ResolveDockerCli();
        bool requiresComposePlugin =
            spec.Command.Arguments.Any(argument =>
                argument == "compose" ||
                argument.Contains(
                    " compose ",
                    StringComparison.Ordinal));
        string? dockerConfigDirectory = requiresComposePlugin
            ? PrepareDockerPluginConfig(ResolveDockerComposeCli())
            : null;
        string socket = state.CurrentEngineSocketPath;
        string fileName;
        IReadOnlyList<string> arguments;
        if (spec.Command.FileName == "/hpd/container-run")
        {
            fileName = docker;
            arguments = TranslateContainerRun(spec.Command.Arguments);
        }
        else if (spec.Command.FileName == "/usr/bin/docker")
        {
            fileName = docker;
            arguments = RewriteArguments(spec.Command.Arguments, docker, socket);
        }
        else if (spec.Command.FileName == "/bin/sh")
        {
            if (spec.Command.Arguments.Count < 3 ||
                spec.Command.Arguments[0] is not ("-ceu" or "-cu") ||
                !AllowedComposeOperations.Contains(
                    spec.Command.Arguments[2]) ||
                spec.Command.Arguments.Any(argument =>
                    argument.Contains('\0')))
            {
                string shellFlags = spec.Command.Arguments.Count > 0
                    ? spec.Command.Arguments[0]
                    : "missing";
                string operation = spec.Command.Arguments.Count > 2
                    ? spec.Command.Arguments[2]
                    : "missing";
                throw new InvalidOperationException(
                    "LocalEnvironment.HostShellRejected: provider-controlled shell invocations require an exact HPDOS Compose operation envelope " +
                    $"(flags='{shellFlags}', operation='{operation}', argumentCount={spec.Command.Arguments.Count}).");
            }
            fileName = "/bin/sh";
            arguments = RewriteArguments(spec.Command.Arguments, docker, socket);
        }
        else
        {
            throw new InvalidOperationException(
                $"LocalEnvironment.ProcessCommandUnsupported: command '{spec.Command.FileName}' is not in the provider-controlled command allowlist.");
        }

        return new LocalResolvedCommand(
            fileName,
            arguments,
            workingDirectory,
            dockerConfigDirectory);
    }

    private string? ResolveWorkingDirectory(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return null;
        string root = Path.GetFullPath(
            state.WorkloadStateRoot);
        string candidate = Path.GetFullPath(requested);
        string relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "LocalEnvironment.HostWorkingDirectoryRejected: provider-controlled processes must remain beneath the provider-private workload-state root.");
        }
        return candidate;
    }

    private void ValidateEngineAuthority(ProcessInvocationSpec spec)
    {
        if (spec.Isolation.AuthorityBindings.Count != 1)
        {
            throw new InvalidOperationException(
                "LocalEnvironment.EngineAuthorityRequired: exactly one current engine authority binding is required.");
        }
        ProviderLedgerLookup<
            ProviderResourceEntry<
                AuthorityBinding,
                AuthorityBindingSpec,
                AuthorityBindingStatus>> lookup = state.Ledger.TryGet<
                    AuthorityBinding,
                    AuthorityBindingSpec,
                    AuthorityBindingStatus>(
                        spec.Isolation.AuthorityBindings[0]);
        if (!lookup.Succeeded ||
            lookup.Entry!.Status.BindingPhase !=
                AuthorityBindingPhase.Projected ||
            !state.IsAuthorityBoundToCurrentEngine(
                lookup.Entry.Resource.Id.Value) ||
            lookup.Entry.Status.BoundAuthority?.ExpiresAt is { } expires &&
                expires <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException(
                lookup.Diagnostic?.Message ??
                "LocalEnvironment.EngineAuthorityStale: the engine authority binding is not current.");
        }
    }

    private IReadOnlyList<string> TranslateContainerRun(
        IReadOnlyList<string> source)
    {
        string? image = null;
        var environment = new List<string>();
        var command = new List<string>();
        for (int index = 0; index < source.Count; index++)
        {
            switch (source[index])
            {
                case "--image" when ++index < source.Count:
                    image = source[index];
                    break;
                case "--engine-socket" when ++index < source.Count:
                    break;
                case "--timeout-ms" when ++index < source.Count:
                    break;
                case "--env" when ++index < source.Count:
                    environment.Add(source[index]);
                    break;
                case "--":
                    command.AddRange(source.Skip(index + 1));
                    index = source.Count;
                    break;
                default:
                    throw new InvalidOperationException(
                        "LocalEnvironment.ContainerRunArgumentInvalid: the provider received an unsupported container-run argument.");
            }
        }
        if (string.IsNullOrWhiteSpace(image))
            throw new InvalidOperationException(
                "LocalEnvironment.ContainerImageRequired: container-run omitted its image.");
        var translated = new List<string>
        {
            "--host",
            $"unix://{state.CurrentEngineSocketPath}",
            "run",
            "--rm",
            "--network",
            "none",
        };
        foreach (string variable in environment)
        {
            translated.Add("--env");
            translated.Add(variable);
        }
        translated.Add(image);
        translated.AddRange(command);
        return translated;
    }

    private IReadOnlyList<string> RewriteArguments(
        IReadOnlyList<string> arguments,
        string docker,
        string socket) =>
        arguments.Select(argument =>
            argument.Replace(
                    "/run/hpd/engine/docker.sock",
                    socket,
                    StringComparison.Ordinal)
                .Replace(
                    "/usr/bin/docker",
                    docker,
                    StringComparison.Ordinal))
            .ToArray();

    private string ResolveDockerCli()
    {
        string[] candidates = string.IsNullOrWhiteSpace(
            state.Options.DockerCliPath)
            ?
            [
                "/usr/local/bin/docker",
                "/opt/homebrew/bin/docker",
                "/usr/bin/docker",
            ]
            : [Path.GetFullPath(state.Options.DockerCliPath)];
        return candidates.FirstOrDefault(File.Exists) ??
            throw new InvalidOperationException(
                "LocalEnvironment.DockerCliUnavailable: configure DockerCliPath or install the Docker CLI in a supported location.");
    }

    private string ResolveDockerComposeCli()
    {
        string[] candidates = string.IsNullOrWhiteSpace(
            state.Options.DockerComposeCliPath)
            ?
            [
                "/Applications/Docker.app/Contents/Resources/cli-plugins/docker-compose",
                "/usr/local/lib/docker/cli-plugins/docker-compose",
                "/opt/homebrew/lib/docker/cli-plugins/docker-compose",
                "/usr/libexec/docker/cli-plugins/docker-compose",
                "/usr/lib/docker/cli-plugins/docker-compose",
            ]
            : [Path.GetFullPath(state.Options.DockerComposeCliPath)];
        return candidates.FirstOrDefault(File.Exists) ??
            throw new InvalidOperationException(
                "LocalEnvironment.DockerComposeCliUnavailable: configure DockerComposeCliPath or install the Docker Compose CLI plugin in a supported location.");
    }

    private string PrepareDockerPluginConfig(string composeCli)
    {
        string directory = Path.Combine(
            state.WorkloadStateRoot,
            ".docker-provider");
        Directory.CreateDirectory(directory);
        string pluginDirectory =
            Path.GetDirectoryName(composeCli) ??
            throw new InvalidOperationException(
                "The Docker Compose CLI plugin path has no parent directory.");
        string configPath = Path.Combine(directory, "config.json");
        using (var stream = new FileStream(
                   configPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None))
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("cliPluginsExtraDirs");
            writer.WriteStringValue(pluginDirectory);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            File.SetUnixFileMode(
                configPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        return directory;
    }

    private LocalProcessOperation Require(
        TargetHandle<ProcessInvocation> handle)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ProcessInvocation,
                ProcessInvocationSpec,
                ProcessInvocationStatus>> lookup = state.Ledger.TryGet<
                    ProcessInvocation,
                    ProcessInvocationSpec,
                    ProcessInvocationStatus>(handle);
        if (!lookup.Succeeded ||
            !_processes.TryGetValue(
                lookup.Entry!.Resource.Id.Value,
                out LocalProcessOperation? operation))
        {
            throw new InvalidOperationException(
                lookup.Diagnostic?.Message ??
                "The Local process is no longer retained.");
        }
        return operation;
    }

    private void StoreStatus(
        ResourceRef<ProcessInvocation> resource,
        ProcessInvocationSpec spec,
        ProcessInvocationStatus status)
    {
        state.Ledger.Upsert(
            new ResourceMetadata<ProcessInvocation>
            {
                Id = resource.Id,
                Kind = new ResourceKind("ProcessInvocation"),
                Scope = resource.Scope,
                Generation =
                    resource.Generation ?? new ResourceGeneration(1),
                SchemaVersion = new SchemaVersion("1"),
            },
            spec,
            status,
            Shape);
    }

    internal sealed record LocalResolvedCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        string? WorkingDirectory,
        string? DockerConfigDirectory);
}

internal sealed class LocalProcessOperation :
    IProcessInvocationHandle
{
    private readonly LocalProviderState _state;
    private readonly LocalProcessProvider.LocalResolvedCommand _command;
    private readonly IProcessOutputSink? _sink;
    private readonly Action<ProcessInvocationStatus> _statusChanged;
    private readonly object _outputGate = new();
    private readonly List<ProcessOutputChunk> _chunks = [];
    private readonly MemoryStream _stdout = new();
    private readonly MemoryStream _stderr = new();
    private readonly TaskCompletionSource<ProcessInvocationResult>
        _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private long _sequence;
    private long _stdoutObserved;
    private long _stderrObserved;
    private int _stopRequested;
    private int _disposed;

    public LocalProcessOperation(
        LocalProviderState state,
        ResourceRef<ProcessInvocation> resource,
        TargetHandle<ProcessInvocation> handle,
        ProcessInvocationSpec spec,
        LocalProcessProvider.LocalResolvedCommand command,
        IProcessOutputSink? sink,
        Action<ProcessInvocationStatus> statusChanged)
    {
        _state = state;
        Resource = resource;
        Handle = handle;
        Spec = spec;
        _command = command;
        _sink = sink;
        _statusChanged = statusChanged;
        Status = new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Reconciling,
            ProcessPhase = ProcessInvocationPhase.Prepared,
            ObservedGeneration =
                resource.Generation ?? new ResourceGeneration(1),
            LastTransitionAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            IoState = ProcessIoState.Open,
            Handle = handle,
        };
    }

    public TargetHandle<ProcessInvocation> Handle { get; }
    public ResourceRef<ProcessInvocation>? Resource { get; }
    public ProcessInvocationSpec Spec { get; }
    public ProcessInvocationStatus Status { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _command.FileName,
            WorkingDirectory =
                _command.WorkingDirectory ??
                _state.WorkloadStateRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment.Clear();
        start.Environment["PATH"] =
            "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin";
        start.Environment["HOME"] = _state.WorkloadStateRoot;
        start.Environment["DOCKER_HOST"] =
            $"unix://{_state.CurrentEngineSocketPath}";
        if (_command.DockerConfigDirectory is { } dockerConfig)
            start.Environment["DOCKER_CONFIG"] = dockerConfig;
        if (Spec.Command.Environment.Count != 0)
            throw new InvalidOperationException(
                "LocalEnvironment.HostProcessEnvironmentRejected: provider-controlled host processes accept only the provider's fixed PATH, HOME, and DOCKER_HOST environment.");
        foreach (string argument in _command.Arguments)
            start.ArgumentList.Add(argument);
        Directory.CreateDirectory(start.WorkingDirectory);

        var process = new Process
        {
            StartInfo = start,
            EnableRaisingEvents = true,
        };
        if (!process.Start())
            throw new InvalidOperationException(
                "The Local provider could not start the process.");
        _process = process;
        Status = Status with
        {
            Phase = ResourcePhase.Ready,
            ProcessPhase = ProcessInvocationPhase.Running,
            SystemProcessId = process.Id,
            ProviderProcessId =
                $"local:{process.Id}",
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        _statusChanged(Status);
        Task stdout = PumpAsync(
            process.StandardOutput.BaseStream,
            ProcessOutputStream.Stdout,
            _stdout,
            Spec.Io.StandardOutput);
        Task stderr = PumpAsync(
            process.StandardError.BaseStream,
            ProcessOutputStream.Stderr,
            _stderr,
            Spec.Io.StandardError);
        if (Spec.Io.StandardInput.Kind == ProcessInputKind.InlineBytes)
        {
            await process.StandardInput.BaseStream.WriteAsync(
                    Spec.Io.StandardInput.InlineBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            process.StandardInput.Close();
        }
        else if (Spec.Io.StandardInput.Kind == ProcessInputKind.None)
        {
            process.StandardInput.Close();
        }
        _ = CompleteAsync(process, stdout, stderr);
    }

    public async ValueTask WriteStdinAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        Process process = _process ??
            throw new InvalidOperationException("The process has not started.");
        await process.StandardInput.BaseStream.WriteAsync(
                bytes,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask CloseStdinAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _process?.StandardInput.Close();
        return ValueTask.CompletedTask;
    }

    public ValueTask SignalAsync(
        ProcessSignal signal,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                signal.Name,
                "kill",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                signal.Name,
                "terminate",
                StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromException(
                new NotSupportedException(
                    "The Local provider supports only terminate/kill signals."));
        }
        return StopAsync(
            new ProcessStopRequest(
                string.Equals(
                    signal.Name,
                    "kill",
                    StringComparison.OrdinalIgnoreCase)
                    ? StopKind.Kill
                    : StopKind.GracefulThenKill),
            cancellationToken);
    }

    public async ValueTask StopAsync(
        ProcessStopRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _stopRequested, 1);
        Process? process = _process;
        if (process is null || process.HasExited)
            return;
        if (request.Kind != StopKind.Kill)
        {
            process.CloseMainWindow();
            TimeSpan grace =
                request.GracePeriod ?? TimeSpan.FromSeconds(2);
            using var deadline =
                new CancellationTokenSource(grace);
            try
            {
                await process.WaitForExitAsync(deadline.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
            }
        }
        process.Kill(entireProcessTree: Spec.Policy.StopProcessTree);
    }

    public ValueTask ResizeTerminalAsync(
        TerminalSpec size,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(
            new NotSupportedException(
                "The Local provider does not expose a host terminal."));

    public async ValueTask<ProcessInvocationResult> WaitAsync(
        CancellationToken cancellationToken = default) =>
        await _completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        ProcessOutputChunk[] chunks;
        lock (_outputGate)
            chunks = _chunks.ToArray();
        foreach (ProcessOutputChunk chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_process is { HasExited: false })
        {
            await StopAsync(
                    new ProcessStopRequest(StopKind.Kill),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        _process?.Dispose();
        _process = null;
        _stdout.Dispose();
        _stderr.Dispose();
    }

    internal async ValueTask<bool> CleanupFailedStartAsync()
    {
        Process? process = _process;
        if (process is null)
            return true;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var deadline =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(deadline.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            Status = Status with
            {
                Phase = ResourcePhase.Failed,
                ProcessPhase = ProcessInvocationPhase.Failed,
                LastTransitionAt = DateTimeOffset.UtcNow,
            };
            _statusChanged(Status);
            return false;
        }
    }

    private async Task PumpAsync(
        Stream source,
        ProcessOutputStream stream,
        MemoryStream captured,
        ProcessOutputSpec policy)
    {
        byte[] buffer = new byte[8192];
        int maximum = policy.MaxCapturedBytes ?? 1024 * 1024;
        while (true)
        {
            int read = await source.ReadAsync(buffer)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            if (stream == ProcessOutputStream.Stdout)
                Interlocked.Add(ref _stdoutObserved, read);
            else
                Interlocked.Add(ref _stderrObserved, read);
            int retained = Math.Min(
                read,
                Math.Max(0, maximum - checked((int)captured.Length)));
            if (retained > 0 && policy.Capture)
                captured.Write(buffer, 0, retained);
            if (policy.Stream || Spec.Io.LogPolicy.RetainOutputEvents)
            {
                var chunk = new ProcessOutputChunk(
                    Handle,
                    stream,
                    Interlocked.Increment(ref _sequence),
                    DateTimeOffset.UtcNow,
                    buffer.AsMemory(0, read).ToArray(),
                    retained < read
                        ? ProcessOutputChunkFlags.Truncated
                        : ProcessOutputChunkFlags.None);
                lock (_outputGate)
                {
                    if (_chunks.Count < 1024)
                        _chunks.Add(chunk);
                }
                if (_sink is not null)
                    await _sink.OnOutputAsync(chunk)
                        .ConfigureAwait(false);
            }
        }
    }

    private async Task CompleteAsync(
        Process process,
        Task stdout,
        Task stderr)
    {
        ProcessCompletionKind completion;
        try
        {
            using var timeout = Spec.Policy.Timeout is { } duration
                ? new CancellationTokenSource(duration)
                : null;
            await process.WaitForExitAsync(
                    timeout?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            completion = Volatile.Read(ref _stopRequested) != 0
                ? ProcessCompletionKind.Stopped
                : ProcessCompletionKind.Exited;
        }
        catch (OperationCanceledException)
        {
            completion = ProcessCompletionKind.TimedOut;
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch
        {
            completion = ProcessCompletionKind.Faulted;
        }
        DateTimeOffset exitedAt = DateTimeOffset.UtcNow;
        int? exitCode = process.HasExited ? process.ExitCode : null;
        ProcessInvocationResult result = Result(
            completion,
            exitCode,
            exitedAt);
        Status = Status with
        {
            Phase = completion == ProcessCompletionKind.Faulted
                ? ResourcePhase.Failed
                : ResourcePhase.Ready,
            ProcessPhase = completion switch
            {
                ProcessCompletionKind.Exited =>
                    ProcessInvocationPhase.Exited,
                ProcessCompletionKind.Stopped or
                ProcessCompletionKind.Killed =>
                    ProcessInvocationPhase.Stopped,
                _ => ProcessInvocationPhase.Failed,
            },
            IoState = ProcessIoState.Closed,
            ExitedAt = exitedAt,
            Result = result,
            LastTransitionAt = exitedAt,
        };
        _statusChanged(Status);
        _completion.TrySetResult(result);
    }

    private ProcessInvocationResult Result(
        ProcessCompletionKind completion,
        int? exitCode,
        DateTimeOffset exitedAt)
    {
        byte[] stdout = _stdout.ToArray();
        byte[] stderr = _stderr.ToArray();
        return new ProcessInvocationResult
        {
            ProcessId = Resource!.Value.Id,
            SystemProcessId = _process?.Id,
            ProviderProcessId = _process is null
                ? null
                : $"local:{_process.Id}",
            ExitCode = exitCode,
            CompletionKind = completion,
            StartedAt = Status.StartedAt,
            ExitedAt = exitedAt,
            Duration = exitedAt - Status.StartedAt,
            Output = new ProcessCapturedOutput
            {
                Stdout = StreamResult(
                    stdout,
                    Interlocked.Read(ref _stdoutObserved)),
                Stderr = StreamResult(
                    stderr,
                    Interlocked.Read(ref _stderrObserved)),
                MergedStandardError = false,
                OutputDrainTimedOut = false,
                OutputDrainTimeout =
                    Spec.Policy.OutputDrainTimeout,
            },
        };
    }

    private static ProcessStreamOutput StreamResult(
        byte[] bytes,
        long observed) =>
        new()
        {
            CapturedBytes = bytes,
            BytesObserved = observed,
            BytesCaptured = bytes.Length,
            BytesDiscarded = checked(observed - bytes.Length),
            Truncated = observed > bytes.Length,
        };

}
