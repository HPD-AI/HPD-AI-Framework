namespace HPD.Environment.AppleVirtualization.Processes;

using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Environment.AppleVirtualization.Authority;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;

public sealed class AppleVirtualizationProcessProvider : IProcessProvider, IRetainedProcessProvider
{
    private const int MaxReadOutputChunksPerCall = 1024;

    private static readonly SchemaVersion SchemaVersion = new("v1");
    private static readonly ResourceKind ProcessKind = new("process-invocation");

    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly IAppleVirtualizationHelperClient _helper;
    private readonly ISandboxPlanner _sandboxPlanner;
    private readonly ConcurrentDictionary<string, long> _lastOutputSequenceByProcess = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AppleVirtualizationProcessHostRoute> _hostRouteByProcess = new(StringComparer.Ordinal);
    private long _processSequence;
    private long _requestSequence;
    private long _stdinSequence;

    internal AppleVirtualizationProcessProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper)
        : this(ledger, helper, new SandboxIsolationPlanner())
    {
    }

    internal AppleVirtualizationProcessProvider(
        AppleVirtualizationProviderStateLedger ledger,
        IAppleVirtualizationHelperClient helper,
        ISandboxPlanner sandboxPlanner)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        _sandboxPlanner = sandboxPlanner ?? throw new ArgumentNullException(nameof(sandboxPlanner));
    }

    public ProviderId ProviderId => AppleVirtualizationProviderDescriptor.ProviderId;

    public async ValueTask<IProcessInvocationHandle> StartAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus>> unitLookup =
            _ledger.TryGetExecutionUnit(spec.Target);
        if (!unitLookup.Succeeded)
        {
            throw ProcessDiagnostics.ToException(unitLookup.Diagnostic, "process.start");
        }

        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit = unitLookup.Entry!;
        string processId = "process-" + Interlocked.Increment(ref _processSequence).ToString(CultureInfo.InvariantCulture);
        ResourceMetadata<ProcessInvocation> metadata = CreateMetadata(unit.Resource.Scope, processId);

        ProcessInvocationStatus starting = new()
        {
            Phase = ResourcePhase.Reconciling,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            ProcessPhase = ProcessInvocationPhase.Prepared,
            IoState = ProcessIoState.Open,
            StartedAt = DateTimeOffset.UtcNow,
        };

        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry =
            _ledger.UpsertProcessInvocation(metadata, starting);

        Diagnostic? precondition = ValidateStartPreconditions(
            unit,
            spec,
            out ProcessProjectionRequirement projection,
            out AppleVirtualizationProcessHostRoute? hostRoute);
        if (precondition is not null)
        {
            ProcessInvocationResult result = FailedResult(entry, spec, ProcessCompletionKind.FailedToStart, precondition);
            ProcessInvocationStatus failed = starting with
            {
                Phase = ResourcePhase.Failed,
                ProcessPhase = ProcessInvocationPhase.Failed,
                IoState = ProcessIoState.Closed,
                Result = result,
                Diagnostics = [precondition],
                ExitedAt = DateTimeOffset.UtcNow,
                LastTransitionAt = DateTimeOffset.UtcNow,
            };
            entry = _ledger.UpsertProcessInvocation(metadata, failed);
            return new AppleVirtualizationProcessInvocationHandle(this, entry.TargetHandle, entry.Resource, spec);
        }

        SandboxPlanEnvelope? sandboxPlan = await CreateSandboxPlanAsync(spec, cancellationToken).ConfigureAwait(false);
        _hostRouteByProcess[processId] = hostRoute!;

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessStart) with
            {
                ResourceKind = ProcessKind,
                ResourceId = processId,
                ResourceScope = metadata.Scope,
                ResourceGeneration = metadata.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = hostRoute,
                ProcessStartRequest = new AppleVirtualizationProcessStartRequest
                {
                    ProcessId = processId,
                    UnitId = unit.Resource.Id.Value,
                    Command = spec.Command,
                    Identity = spec.Identity,
                    Limits = spec.Limits,
                    Io = spec.Io,
                    Policy = spec.Policy,
                    Isolation = spec.Isolation,
                    SandboxPlan = sandboxPlan,
                    RequiredProjectionId = projection.ProjectionId,
                    RequiredProjectionGuestPath = projection.GuestPath,
                    RequireVerifiedProjection = projection.Required,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ProcessDiagnostics.ToDiagnostic(response.Error, "process.start");
            ProcessInvocationResult result = FailedResult(entry, spec, ProcessCompletionKind.FailedToStart, diagnostic);
            ProcessInvocationStatus failed = starting with
            {
                Phase = ResourcePhase.Failed,
                ProcessPhase = ProcessInvocationPhase.Failed,
                Result = result,
                Diagnostics = [diagnostic],
                ExitedAt = DateTimeOffset.UtcNow,
            };
            entry = _ledger.UpsertProcessInvocation(metadata, failed);
            return new AppleVirtualizationProcessInvocationHandle(this, entry.TargetHandle, entry.Resource, spec);
        }

        AppleVirtualizationProcessStatusResponse? status = response.ProcessStatusResponse;
        ProcessInvocationStatus running = starting with
        {
            Phase = ResourcePhase.Ready,
            ProcessPhase = status?.ProcessPhase is ProcessInvocationPhase.Unknown or null
                ? ProcessInvocationPhase.Running
                : status.ProcessPhase,
            IoState = status?.IoState is ProcessIoState.Unknown or null ? ProcessIoState.Open : status.IoState,
            ProviderProcessId = status?.ProviderProcessId,
            SystemProcessId = status?.SystemProcessId,
            Conditions = status?.Conditions ?? Array.Empty<Condition>(),
        };

        entry = _ledger.UpsertProcessInvocation(metadata, running);
        _ledger.AttachProcessToExecutionUnit(unit.Resource, entry.Resource);

        if (spec.Io.StandardInput.Kind == ProcessInputKind.InlineBytes)
        {
            await SendStdinAsync(entry, spec.Io.StandardInput.InlineBytes, closeAfterWrite: true, cancellationToken)
                .ConfigureAwait(false);
        }

        return new AppleVirtualizationProcessInvocationHandle(this, entry.TargetHandle, entry.Resource, spec);
    }

    private async ValueTask<SandboxPlanEnvelope?> CreateSandboxPlanAsync(
        ProcessInvocationSpec spec,
        CancellationToken cancellationToken)
    {
        if (spec.Isolation.Mode is not ProcessIsolationMode.Isolated)
            return null;

        return await _sandboxPlanner.PlanAsync(
            spec,
            new SandboxExecutionContext
            {
                HostPlatform = CurrentHostPlatform(),
                ExecutionPlatform = new PlatformSpec("linux", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()),
                EnforcementLocation = SandboxEnforcementLocation.Guest,
                Scope = spec.Target.Route.Scope,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static PlatformSpec CurrentHostPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    public async ValueTask<ProcessInvocationResult> RunAsync(
        ProcessInvocationSpec spec,
        IProcessOutputSink? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        AppleVirtualizationProcessInvocationHandle? handle = null;
        try
        {
            handle = (AppleVirtualizationProcessInvocationHandle)await StartAsync(spec, output, cancellationToken).ConfigureAwait(false);

            if (TryGetStoredResult(handle.Handle, out ProcessInvocationResult? stored))
            {
                return stored;
            }

            ProcessOutputCapture capture = new(spec.Io, spec.Policy.OutputDrainTimeout);
            await foreach (ProcessOutputChunk chunk in ReadOutputAsync(handle.Handle, cancellationToken).ConfigureAwait(false))
            {
                ProcessOutputChunk normalized = capture.Record(chunk);
                if (output is not null && ShouldStream(spec.Io, normalized.Stream))
                {
                    await output.OnOutputAsync(normalized, cancellationToken).ConfigureAwait(false);
                }
            }

            ProcessInvocationResult waited = await WaitAsync(handle.Handle, spec.Policy.Timeout, cancellationToken).ConfigureAwait(false);
            ProcessInvocationResult result = waited with
            {
                Output = capture.HasEvents
                    ? capture.ToOutput(waited.Output.OutputDrainTimedOut)
                    : BoundStoredOutput(waited.Output, spec.Io),
            };

            UpdateResult(handle.Handle, result);
            return result;
        }
        catch (OperationCanceledException) when (handle is not null)
        {
            AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(handle.Handle);
            ProcessCompletionKind completionKind = cancellationToken.IsCancellationRequested
                ? ProcessCompletionKind.Cancelled
                : ProcessCompletionKind.TimedOut;
            Diagnostic diagnostic = completionKind == ProcessCompletionKind.Cancelled
                ? ProcessDiagnostics.RunCancelled(entry.Resource.Id.Value)
                : ProcessDiagnostics.RunTimedOut(entry.Resource.Id.Value, spec.Policy.Timeout);

            if (spec.Policy.StopOnRunCancellation)
            {
                await TryStopForRunCancellationAsync(entry, spec.Policy.Stop, completionKind, cancellationToken).ConfigureAwait(false);
            }

            ProcessInvocationResult result = FailedResult(entry, spec, completionKind, diagnostic);
            Update(entry, entry.Status with
            {
                Phase = ResourcePhase.Ready,
                ProcessPhase = ProcessInvocationPhase.Stopped,
                IoState = ProcessIoState.Closed,
                Result = result,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                ExitedAt = result.ExitedAt,
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return result;
        }
    }

    public async ValueTask SignalAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessSignal signal,
        CancellationToken cancellationToken = default)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        if (IsTerminal(entry.Status))
        {
            throw ProcessDiagnostics.ToException(ProcessDiagnostics.AlreadyExited(entry.Resource.Id.Value, "process.signal"));
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessSignal) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessSignalRequest = new AppleVirtualizationProcessSignalRequest(entry.Resource.Id.Value, signal),
            },
            cancellationToken).ConfigureAwait(false);

        if (TryHandleHelperError(entry, response, "process.signal", out Diagnostic? diagnostic))
        {
            throw ProcessDiagnostics.ToException(diagnostic);
        }
    }

    public ValueTask ResizeTerminalAsync(
        TargetHandle<ProcessInvocation> process,
        TerminalSpec size,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = ResolveProcess(process);
        throw ProcessDiagnostics.UnsupportedResize();
    }

    public async ValueTask<ProcessInvocationResult> WaitAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        return await WaitAsync(process, timeout: null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ProcessInvocationResult> WaitAsync(
        TargetHandle<ProcessInvocation> process,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        if (entry.Status.Result is { } stored &&
            entry.Status.ProcessPhase is ProcessInvocationPhase.Failed or ProcessInvocationPhase.Exited or ProcessInvocationPhase.Stopped)
        {
            return stored;
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessWait) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
                {
                    ProcessId = entry.Resource.Id.Value,
                    Timeout = timeout,
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            Diagnostic diagnostic = ProcessDiagnostics.ToDiagnostic(response.Error, "process.wait");
            ProcessCompletionKind completionKind = CompletionKindFromHelperError(diagnostic);
            ProcessInvocationResult failed = FailedResult(entry, null, completionKind, diagnostic);
            Update(entry, entry.Status with
            {
                Phase = completionKind == ProcessCompletionKind.Faulted ? ResourcePhase.Failed : ResourcePhase.Ready,
                ProcessPhase = CompletionToPhase(completionKind, null),
                IoState = ProcessIoState.Closed,
                Result = failed,
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                ExitedAt = DateTimeOffset.UtcNow,
            });
            return failed;
        }

        AppleVirtualizationProcessStatusResponse? status = response.ProcessStatusResponse;
        ProcessInvocationResult result = status?.Result ?? CompletedResult(entry, status);
        ProcessInvocationPhase phase = CompletionToPhase(result.CompletionKind, status?.ProcessPhase);
        ResourcePhase resourcePhase = phase == ProcessInvocationPhase.Failed ? ResourcePhase.Failed : ResourcePhase.Ready;

        Update(entry, entry.Status with
        {
            Phase = resourcePhase,
            ProcessPhase = phase,
            IoState = status?.IoState ?? ProcessIoState.Closed,
            ProviderProcessId = status?.ProviderProcessId ?? result.ProviderProcessId ?? entry.Status.ProviderProcessId,
            SystemProcessId = status?.SystemProcessId ?? result.SystemProcessId ?? entry.Status.SystemProcessId,
            Result = result,
            Conditions = status?.Conditions ?? entry.Status.Conditions,
            ExitedAt = result.ExitedAt ?? DateTimeOffset.UtcNow,
            LastTransitionAt = DateTimeOffset.UtcNow,
        });

        return result;
    }

    private bool TryGetStoredResult(
        TargetHandle<ProcessInvocation> handle,
        out ProcessInvocationResult? result)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(handle);
        result = entry.Status.Result;
        return result is not null &&
            entry.Status.ProcessPhase is ProcessInvocationPhase.Failed or ProcessInvocationPhase.Exited or ProcessInvocationPhase.Stopped;
    }

    public async IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
        TargetHandle<ProcessInvocation> process,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        string processId = entry.Resource.Id.Value;
        long afterSequence = _lastOutputSequenceByProcess.GetOrAdd(processId, 0);
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessReadOutput) with
            {
                ResourceKind = ProcessKind,
                ResourceId = processId,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
                {
                    ProcessId = processId,
                    AfterOutputSequence = afterSequence == 0 ? null : afterSequence,
                    OutputLimit = MaxReadOutputChunksPerCall,
                },
            },
            cancellationToken).ConfigureAwait(false);

        ThrowIfHelperError(response, "process.readOutput");

        if (TryCreateOutputChunk(entry, response.ProcessOutputEvent, out ProcessOutputChunk responseChunk))
        {
            RecordLastOutputSequence(processId, responseChunk.Sequence);
            yield return responseChunk;
        }

        await foreach (AppleVirtualizationHelperEnvelope helperEvent in _helper
            .ReadEventsAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!TryCreateOutputChunk(entry, helperEvent.ProcessOutputEvent, out ProcessOutputChunk outputChunk))
            {
                continue;
            }
            RecordLastOutputSequence(processId, outputChunk.Sequence);
            yield return outputChunk;
        }
    }

    internal async ValueTask WriteStdinAsync(
        TargetHandle<ProcessInvocation> process,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        await SendStdinAsync(entry, bytes, closeAfterWrite: false, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask CloseStdinAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        await SendStdinAsync(entry, ReadOnlyMemory<byte>.Empty, closeAfterWrite: true, cancellationToken).ConfigureAwait(false);
        Update(entry, entry.Status with
        {
            IoState = ProcessIoState.InputClosed,
            LastTransitionAt = DateTimeOffset.UtcNow,
        });
    }

    public async ValueTask<ProcessInvocationStatus> GetStatusAsync(
        TargetHandle<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry =
            ResolveProcess(process);
        if (entry.Status.Result is not null &&
            IsTerminal(entry.Status.ProcessPhase))
        {
            return entry.Status;
        }
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessStatus) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
                {
                    ProcessId = entry.Resource.Id.Value,
                },
            },
            cancellationToken).ConfigureAwait(false);
        ThrowIfHelperError(response, "process.status");
        AppleVirtualizationProcessStatusResponse status = response.ProcessStatusResponse ??
            throw ProcessDiagnostics.ToException(new Diagnostic
            {
                Code = new DiagnosticCode("AppleVirtualization.ProcessStatusMalformed"),
                Message = "The helper returned no process status.",
                Severity = DiagnosticSeverity.Error,
            });
        ValidateObservedIdentity(entry, response, status);

        ProcessInvocationStatus observed = entry.Status with
        {
            Phase = status.ProcessPhase == ProcessInvocationPhase.Failed
                ? ResourcePhase.Failed
                : ResourcePhase.Ready,
            ProcessPhase = status.ProcessPhase,
            IoState = status.IoState,
            ProviderProcessId = status.ProviderProcessId,
            SystemProcessId = status.SystemProcessId,
            Result = status.Result ?? entry.Status.Result,
            Conditions = status.Conditions,
            ExitedAt = IsTerminal(status.ProcessPhase)
                ? status.Result?.ExitedAt ?? entry.Status.ExitedAt ?? DateTimeOffset.UtcNow
                : entry.Status.ExitedAt,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        Update(entry, observed);
        return observed;
    }

    public async ValueTask StopAsync(
        TargetHandle<ProcessInvocation> process,
        ProcessStopRequest request,
        CancellationToken cancellationToken)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(process);
        if (IsTerminal(entry.Status))
        {
            return;
        }

        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(AppleVirtualizationHelperOperation.ProcessStop) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessStopRequest = new AppleVirtualizationProcessStopRequest(
                    entry.Resource.Id.Value,
                    request.Kind,
                    request.GracePeriod,
                    request.Reason),
            },
            cancellationToken).ConfigureAwait(false);

        if (TryHandleHelperError(entry, response, "process.stop", out Diagnostic? diagnostic))
        {
            throw ProcessDiagnostics.ToException(diagnostic);
        }

        ProcessInvocationResult? result = response.ProcessStatusResponse?.Result;
        Update(entry, entry.Status with
        {
            Phase = ResourcePhase.Ready,
            ProcessPhase = result is not null
                ? CompletionToPhase(result.CompletionKind, response.ProcessStatusResponse?.ProcessPhase)
                : response.ProcessStatusResponse?.ProcessPhase ?? ProcessInvocationPhase.Stopping,
            IoState = response.ProcessStatusResponse?.IoState ?? entry.Status.IoState,
            Result = result ?? entry.Status.Result,
            ExitedAt = result?.ExitedAt ?? entry.Status.ExitedAt,
            LastTransitionAt = DateTimeOffset.UtcNow,
        });
    }

    public ValueTask ReleaseAsync(
        ResourceRef<ProcessInvocation> process,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> lookup =
            _ledger.TryGetProcessInvocation(process);
        if (!lookup.Succeeded)
        {
            throw ProcessDiagnostics.ToException(lookup.Diagnostic, "process.release");
        }
        if (!IsTerminal(lookup.Entry!.Status))
        {
            throw ProcessDiagnostics.ToException(
                ProcessDiagnostics.AlreadyExited(process.Id.Value, "process.release") with
                {
                    Code = new DiagnosticCode("AppleVirtualization.ProcessStillRunning"),
                    Message = $"Process '{process.Id.Value}' must stop before its retained resource can be released.",
                });
        }
        _lastOutputSequenceByProcess.TryRemove(process.Id.Value, out _);
        _hostRouteByProcess.TryRemove(process.Id.Value, out _);
        _ledger.RemoveProcessInvocation(process);
        return ValueTask.CompletedTask;
    }

    private async ValueTask SendStdinAsync(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        ReadOnlyMemory<byte> bytes,
        bool closeAfterWrite,
        CancellationToken cancellationToken)
    {
        if (IsTerminal(entry.Status))
        {
            throw ProcessDiagnostics.ToException(ProcessDiagnostics.AlreadyExited(entry.Resource.Id.Value, "process.stdin"));
        }

        AppleVirtualizationHelperOperation operation = closeAfterWrite && bytes.IsEmpty
            ? AppleVirtualizationHelperOperation.ProcessCloseStdin
            : AppleVirtualizationHelperOperation.ProcessStdin;
        AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
            Request(operation) with
            {
                ResourceKind = ProcessKind,
                ResourceId = entry.Resource.Id.Value,
                ResourceScope = entry.Resource.Scope,
                ResourceGeneration = entry.Resource.Generation,
                ProviderHandle = entry.ProviderHandle,
                ProviderGeneration = _ledger.ProviderGeneration,
                ProcessHost = Route(entry),
                ProcessStdinRequest = new AppleVirtualizationProcessStdinRequest
                {
                    ProcessId = entry.Resource.Id.Value,
                    Bytes = bytes,
                    CloseAfterWrite = closeAfterWrite,
                    Sequence = Interlocked.Increment(ref _stdinSequence),
                },
            },
            cancellationToken).ConfigureAwait(false);

        if (TryHandleHelperError(entry, response, AppleVirtualizationHelperOperationNames.ToWireName(operation), out Diagnostic? diagnostic))
        {
            throw ProcessDiagnostics.ToException(diagnostic);
        }
    }

    private async ValueTask TryStopForRunCancellationAsync(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        StopPolicy policy,
        ProcessCompletionKind completionKind,
        CancellationToken cancellationToken)
    {
        if (IsTerminal(entry.Status))
        {
            return;
        }

        _ = cancellationToken;
        using CancellationTokenSource stopCts = new(policy.GracePeriod);
        stopCts.CancelAfter(policy.GracePeriod);
        try
        {
            AppleVirtualizationHelperEnvelope response = await _helper.SendAsync(
                Request(AppleVirtualizationHelperOperation.ProcessStop) with
                {
                    ResourceKind = ProcessKind,
                    ResourceId = entry.Resource.Id.Value,
                    ResourceScope = entry.Resource.Scope,
                    ResourceGeneration = entry.Resource.Generation,
                    ProviderHandle = entry.ProviderHandle,
                    ProviderGeneration = _ledger.ProviderGeneration,
                    ProcessHost = Route(entry),
                    ProcessStopRequest = new AppleVirtualizationProcessStopRequest(
                        entry.Resource.Id.Value,
                        policy.Kind,
                        policy.GracePeriod,
                        completionKind == ProcessCompletionKind.TimedOut ? "run-timeout" : "run-cancelled"),
                },
                stopCts.Token).ConfigureAwait(false);

            _ = TryHandleHelperError(entry, response, "process.stop", out _);
        }
        catch (OperationCanceledException)
        {
            // Cancellation already owns the final RunAsync result; stop failure is represented by that result.
        }
    }

    private AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> ResolveProcess(
        TargetHandle<ProcessInvocation> process)
    {
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus>> lookup =
            _ledger.TryGetProcessInvocation(process);
        if (!lookup.Succeeded)
        {
            throw ProcessDiagnostics.ToException(lookup.Diagnostic, "process.handle");
        }

        return lookup.Entry!;
    }

    private void UpdateResult(TargetHandle<ProcessInvocation> handle, ProcessInvocationResult result)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry = ResolveProcess(handle);
        Update(entry, entry.Status with
        {
            Result = result,
            ProcessPhase = CompletionToPhase(result.CompletionKind, entry.Status.ProcessPhase),
            ExitedAt = result.ExitedAt ?? entry.Status.ExitedAt,
            LastTransitionAt = DateTimeOffset.UtcNow,
        });
    }

    private AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> Update(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        ProcessInvocationStatus status)
    {
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> updated =
            _ledger.UpsertProcessInvocation(ToMetadata(entry), status);
        if (IsTerminal(status))
        {
            _ledger.DetachProcessFromAllExecutionUnits(updated.Resource);
        }

        return updated;
    }

    private Diagnostic? ValidateStartPreconditions(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        ProcessInvocationSpec spec,
        out ProcessProjectionRequirement projection,
        out AppleVirtualizationProcessHostRoute? hostRoute)
    {
        projection = ResolveProjectionRequirement(unit, spec.Command.WorkingDirectory);
        hostRoute = null;

        if (unit.Status.Phase != ResourcePhase.Ready ||
            (unit.Status.UnitPhase != ExecutionUnitPhase.Ready && unit.Status.UnitPhase != ExecutionUnitPhase.Running))
        {
            return ProcessDiagnostics.GuestNotReady(unit.Resource.Id.Value, spec.Command.FileName);
        }

        if (unit.Status.AssignedHost is not { } assignedHost)
        {
            return ProcessDiagnostics.GuestNotReady(unit.Resource.Id.Value, spec.Command.FileName);
        }
        else
        {
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> hostLookup =
                _ledger.TryGetRuntimeHost(assignedHost);
            if (!hostLookup.Succeeded)
            {
                return hostLookup.Diagnostic;
            }

            RuntimeHostStatus host = hostLookup.Entry!.Status;
            if (host.Readiness?.Ready != true)
            {
                return ProcessDiagnostics.GuestNotReady(unit.Resource.Id.Value, spec.Command.FileName);
            }
            (string? guestBootId, ulong guestBootGeneration) =
                ParseGuestBootGeneration(host.Generations.GuestBootGeneration);
            hostRoute = new AppleVirtualizationProcessHostRoute
            {
                HostId = assignedHost.Id.Value,
                HostStartGeneration = checked((ulong)(host.Generations.HostStartGeneration?.Value ?? 0)),
                GuestBootId = guestBootId,
                GuestBootGeneration = guestBootGeneration,
                GuestAgentGeneration = ParseGuestAgentGeneration(host.Conditions),
            };
        }

        if (projection.Required && !projection.Verified)
        {
            return ProcessDiagnostics.ProjectionNotReady(
                projection.ProjectionId ?? "unknown-projection",
                projection.GuestPath ?? spec.Command.WorkingDirectory ?? "unknown-workdir");
        }

        Diagnostic? authorityDiagnostic = ValidateAuthorityBindings(unit, spec);
        if (authorityDiagnostic is not null)
        {
            return authorityDiagnostic;
        }

        return null;
    }

    private AppleVirtualizationProcessHostRoute Route(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry) =>
        _hostRouteByProcess.TryGetValue(entry.Resource.Id.Value, out AppleVirtualizationProcessHostRoute? route)
            ? route
            : throw ProcessDiagnostics.ToException(new Diagnostic
            {
                Code = new DiagnosticCode("AppleVirtualization.ProcessHostRouteMissing"),
                Message = $"Process '{entry.Resource.Id.Value}' has no accepted runtime-host route.",
                Severity = DiagnosticSeverity.Error,
            });

    private static (string? GuestBootId, ulong Generation) ParseGuestBootGeneration(
        GuestBootGeneration? generation)
    {
        string? value = generation?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, 0);
        }
        int separator = value.LastIndexOf(':');
        string numeric = separator >= 0 ? value[(separator + 1)..] : value;
        return ulong.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            ? (separator > 0 ? value[..separator] : null, parsed)
            : (null, 0);
    }

    private static ulong ParseGuestAgentGeneration(IReadOnlyList<Condition> conditions)
    {
        foreach (Condition condition in conditions)
        {
            if (string.Equals(
                    condition.Type,
                    "AppleVirtualization.GuestAgentGeneration",
                    StringComparison.Ordinal) &&
                ulong.TryParse(condition.Message, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
            {
                return parsed;
            }
        }
        return 0;
    }

    private Diagnostic? ValidateAuthorityBindings(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        ProcessInvocationSpec spec)
    {
        IReadOnlyList<ResourceRef<AuthorityBinding>> bindings = spec.Isolation.AuthorityBindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            ResourceRef<AuthorityBinding> binding = bindings[i];
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<AuthorityBinding, AuthorityBindingStatus>> lookup =
                _ledger.TryGetAuthorityBinding(binding);
            if (!lookup.Succeeded || lookup.Entry is null)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    lookup.Diagnostic?.Message ?? "Authority binding could not be resolved before process start.");
            }

            AuthorityBindingStatus status = lookup.Entry.Status;
            if (status.Phase != ResourcePhase.Ready ||
                status.BindingPhase != AuthorityBindingPhase.Projected ||
                status.BoundAuthority is null)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Authority binding is not projected before process start.");
            }

            BoundAuthority bound = status.BoundAuthority;
            if (bound.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Authority binding lease expired before process start.");
            }

            if (bound.RevocationStatus != RevocationVerificationStatus.Pending)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Authority binding has been revoked or is no longer verified as active before process start.");
            }

            AuthorityBindingSpec? authoritySpec = _ledger.TryGetAuthorityBindingSpec(binding);
            if (authoritySpec is null)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Authority binding spec could not be resolved before process start.");
            }

            AppleVirtualizationAuthoritySourceClassification classification =
                AppleVirtualizationAuthoritySourceClassifier.Classify(authoritySpec);
            if (AppleVirtualizationAuthorityBindingProvider.ValidateProjectionPolicy(
                    AuthorityBindingMetadata(binding),
                    authoritySpec,
                    classification,
                    _ledger) is { } diagnostic)
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(binding.Id.Value, diagnostic.Message);
            }

            if (authoritySpec.Target.Kind != AuthorityTargetKind.ExecutionUnit ||
                authoritySpec.Target.Unit is not { } targetUnit ||
                !HandleTargetsExecutionUnit(targetUnit, unit.Resource))
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Process isolation authority bindings must target the owning execution unit before process start.");
            }

            if (!Contains(unit.Status.AuthorityBindings, binding))
            {
                return ProcessDiagnostics.AuthorityBindingNotReady(
                    binding.Id.Value,
                    "Authority binding is projected but is not attached to the owning execution unit status.");
            }
        }

        return null;
    }

    private static ResourceMetadata<AuthorityBinding> AuthorityBindingMetadata(ResourceRef<AuthorityBinding> binding) =>
        new()
        {
            Id = binding.Id,
            Kind = new ResourceKind("authority-binding"),
            Scope = binding.Scope,
            Generation = binding.Generation ?? default,
            SchemaVersion = SchemaVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static bool HandleTargetsExecutionUnit(TargetHandle<ExecutionUnit> handle, ResourceRef<ExecutionUnit> unit) =>
        string.Equals(handle.Route.BackingResourceId, unit.Id.Value, StringComparison.Ordinal) &&
        string.Equals(handle.Route.Scope.Value, unit.Scope.Value, StringComparison.Ordinal);

    private static bool Contains<TResource>(
        IReadOnlyList<ResourceRef<TResource>> values,
        ResourceRef<TResource> value)
        where TResource : IExecutionResourceMarker
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i].Id.Value, value.Id.Value, StringComparison.Ordinal) &&
                string.Equals(values[i].Scope.Value, value.Scope.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private ProcessProjectionRequirement ResolveProjectionRequirement(
        AppleVirtualizationLedgerEntry<ExecutionUnit, ExecutionUnitStatus> unit,
        string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || unit.Status.RealizedContentProjections.Count == 0)
        {
            return default;
        }

        for (int i = 0; i < unit.Status.RealizedContentProjections.Count; i++)
        {
            ResourceRef<ContentProjection> projectionRef = unit.Status.RealizedContentProjections[i];
            AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<ContentProjection, ContentProjectionStatus>> lookup =
                _ledger.TryGetContentProjection(projectionRef);
            if (!lookup.Succeeded)
            {
                continue;
            }

            ContentProjectionStatus status = lookup.Entry!.Status;
            for (int viewIndex = 0; viewIndex < status.Views.Count; viewIndex++)
            {
                GuestPath? guestPath = status.Views[viewIndex].GuestPath;
                if (guestPath is null || !PathContains(guestPath.Value.Value, workingDirectory))
                {
                    continue;
                }

                bool verified = status.Phase == ResourcePhase.Ready &&
                    status.ProjectionPhase == ContentProjectionPhase.Projected;
                return new ProcessProjectionRequirement(
                    Required: true,
                    Verified: verified,
                    ProjectionId: projectionRef.Id.Value,
                    GuestPath: guestPath.Value.Value);
            }
        }

        return default;
    }

    private static bool PathContains(string guestRoot, string workingDirectory)
    {
        if (string.Equals(guestRoot, workingDirectory, StringComparison.Ordinal))
        {
            return true;
        }

        if (guestRoot.Length == 0 || guestRoot[^1] == '/')
        {
            return workingDirectory.StartsWith(guestRoot, StringComparison.Ordinal);
        }

        return workingDirectory.Length > guestRoot.Length &&
            workingDirectory[guestRoot.Length] == '/' &&
            workingDirectory.StartsWith(guestRoot, StringComparison.Ordinal);
    }

    private ResourceMetadata<ProcessInvocation> CreateMetadata(ResourceScope scope, string processId) =>
        new()
        {
            Id = new ResourceId<ProcessInvocation>(processId),
            Kind = ProcessKind,
            Scope = scope,
            Generation = new ResourceGeneration(1),
            SchemaVersion = SchemaVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static ResourceMetadata<ProcessInvocation> ToMetadata(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry) =>
        new()
        {
            Id = entry.Resource.Id,
            Kind = ProcessKind,
            Scope = entry.Resource.Scope,
            Generation = entry.Resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = SchemaVersion,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation)
    {
        long sequence = Interlocked.Increment(ref _requestSequence);
        return AppleVirtualizationHelperEnvelope.Request(
            operation,
            "apple-vz-process-" + sequence.ToString(CultureInfo.InvariantCulture),
            sequence,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema);
    }

    private void ValidateObservedIdentity(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        AppleVirtualizationHelperEnvelope response,
        AppleVirtualizationProcessStatusResponse status)
    {
        bool valid =
            response.ProviderGeneration == _ledger.ProviderGeneration &&
            string.Equals(response.ResourceId, entry.Resource.Id.Value, StringComparison.Ordinal) &&
            Nullable.Equals(response.ResourceScope, entry.Resource.Scope) &&
            Nullable.Equals(response.ResourceGeneration, entry.Resource.Generation) &&
            Nullable.Equals(response.ProviderHandle, entry.ProviderHandle) &&
            string.Equals(status.ProcessId, entry.Resource.Id.Value, StringComparison.Ordinal);
        if (!valid)
        {
            throw ProcessDiagnostics.ToException(new Diagnostic
            {
                Code = new DiagnosticCode("AppleVirtualization.ProcessStatusIdentityMismatch"),
                Message = $"Process status for '{entry.Resource.Id.Value}' returned stale or mismatched identity.",
                Severity = DiagnosticSeverity.Error,
            });
        }
    }

    private static ProcessInvocationResult CompletedResult(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        AppleVirtualizationProcessStatusResponse? status) =>
        new()
        {
            ProcessId = entry.Resource.Id,
            SystemProcessId = status?.SystemProcessId ?? entry.Status.SystemProcessId,
            ProviderProcessId = status?.ProviderProcessId ?? entry.Status.ProviderProcessId,
            ExitCode = null,
            CompletionKind = status?.ProcessPhase == ProcessInvocationPhase.Failed
                ? ProcessCompletionKind.Faulted
                : ProcessCompletionKind.Exited,
            StartedAt = entry.Status.StartedAt,
            ExitedAt = DateTimeOffset.UtcNow,
            Output = EmptyOutput(),
            Diagnostics = status?.Conditions is { Count: > 0 }
                ? ConditionsFromStatus(status.Conditions)
                : Array.Empty<Condition>(),
        };

    private static ProcessInvocationResult FailedResult(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        ProcessInvocationSpec? spec,
        ProcessCompletionKind completionKind,
        Diagnostic diagnostic) =>
        new()
        {
            ProcessId = entry.Resource.Id,
            SystemProcessId = entry.Status.SystemProcessId,
            ProviderProcessId = entry.Status.ProviderProcessId,
            ExitCode = null,
            CompletionKind = completionKind,
            StartedAt = entry.Status.StartedAt,
            ExitedAt = DateTimeOffset.UtcNow,
            Output = EmptyOutput(spec?.Policy.OutputDrainTimeout),
            Diagnostics = [ProcessDiagnostics.ToCondition(diagnostic)],
        };

    private static ProcessCapturedOutput EmptyOutput(TimeSpan? outputDrainTimeout = null) =>
        new()
        {
            Stdout = new ProcessStreamOutput(),
            Stderr = new ProcessStreamOutput(),
            MergedStandardError = false,
            OutputDrainTimedOut = false,
            OutputDrainTimeout = outputDrainTimeout ?? TimeSpan.Zero,
        };

    private static ProcessInvocationPhase CompletionToPhase(
        ProcessCompletionKind completionKind,
        ProcessInvocationPhase? helperPhase) =>
        helperPhase is not null and not ProcessInvocationPhase.Unknown
            ? helperPhase.Value
            : completionKind switch
            {
                ProcessCompletionKind.FailedToStart or ProcessCompletionKind.Faulted => ProcessInvocationPhase.Failed,
                ProcessCompletionKind.Stopped or ProcessCompletionKind.Cancelled => ProcessInvocationPhase.Stopped,
                ProcessCompletionKind.Killed => ProcessInvocationPhase.Stopped,
                _ => ProcessInvocationPhase.Exited,
            };

    private static ProcessCompletionKind CompletionKindFromHelperError(Diagnostic diagnostic)
    {
        string code = diagnostic.Code.Value;
        if (code.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessCompletionKind.TimedOut;
        }

        if (code.Contains("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessCompletionKind.Cancelled;
        }

        if (code.Contains("Stopped", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("VmStop", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("VmStopped", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessCompletionKind.Stopped;
        }

        if (code.Contains("Killed", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessCompletionKind.Killed;
        }

        return ProcessCompletionKind.Faulted;
    }

    private static bool IsTerminal(ProcessInvocationStatus status) =>
        status.Result is not null ||
        IsTerminal(status.ProcessPhase);

    private static bool IsTerminal(ProcessInvocationPhase phase) =>
        phase is ProcessInvocationPhase.Exited or ProcessInvocationPhase.Failed or ProcessInvocationPhase.Stopped;

    private static bool ShouldStream(ProcessIoSpec io, ProcessOutputStream stream) =>
        stream == ProcessOutputStream.Stdout ? io.StandardOutput.Stream : io.StandardError.Stream;

    private static bool TryCreateOutputChunk(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        AppleVirtualizationProcessOutputEvent? output,
        out ProcessOutputChunk chunk)
    {
        if (output is null || !string.Equals(output.ProcessId, entry.Resource.Id.Value, StringComparison.Ordinal))
        {
            chunk = default;
            return false;
        }

        chunk = new ProcessOutputChunk(
            entry.TargetHandle,
            output.Stream,
            output.Sequence,
            output.ObservedAt,
            output.Bytes,
            output.Flags);
        return true;
    }

    private void RecordLastOutputSequence(string processId, long sequence)
    {
        _lastOutputSequenceByProcess.AddOrUpdate(
            processId,
            sequence,
            (_, current) => sequence > current ? sequence : current);
    }

    private static IReadOnlyList<Diagnostic> AppendDiagnostic(IReadOnlyList<Diagnostic> existing, Diagnostic diagnostic)
    {
        Diagnostic[] diagnostics = new Diagnostic[existing.Count + 1];
        for (int i = 0; i < existing.Count; i++)
        {
            diagnostics[i] = existing[i];
        }

        diagnostics[^1] = diagnostic;
        return diagnostics;
    }

    private static IReadOnlyList<Condition> ConditionsFromStatus(IReadOnlyList<Condition> conditions)
    {
        if (conditions.Count == 0)
        {
            return Array.Empty<Condition>();
        }

        Condition[] copy = new Condition[conditions.Count];
        for (int i = 0; i < conditions.Count; i++)
        {
            copy[i] = conditions[i];
        }

        return copy;
    }

    private readonly record struct ProcessProjectionRequirement(
        bool Required,
        bool Verified,
        string? ProjectionId,
        string? GuestPath);

    private static ProcessCapturedOutput BoundStoredOutput(
        ProcessCapturedOutput output,
        ProcessIoSpec io)
    {
        bool mergedStandardError = output.MergedStandardError;
        return output with
        {
            Stdout = BoundStoredStream(output.Stdout, io.StandardOutput),
            Stderr = mergedStandardError
                ? new ProcessStreamOutput()
                : BoundStoredStream(output.Stderr, io.StandardError),
            MergedStandardError = mergedStandardError,
        };
    }

    private static ProcessStreamOutput BoundStoredStream(
        ProcessStreamOutput output,
        ProcessOutputSpec spec)
    {
        long observed = Math.Max(output.BytesObserved, output.CapturedBytes.Length);
        long limit = spec.MaxCapturedBytes is null
            ? long.MaxValue
            : Math.Max(0, spec.MaxCapturedBytes.Value);
        int capturedLength = !spec.Capture || limit == 0
            ? 0
            : (int)Math.Min(output.CapturedBytes.Length, limit);
        ReadOnlyMemory<byte> captured = capturedLength == 0
            ? ReadOnlyMemory<byte>.Empty
            : output.CapturedBytes[..capturedLength].ToArray();
        long discarded = Math.Max(output.BytesDiscarded, observed - capturedLength);
        bool boundTruncated = spec.Capture && observed > capturedLength;
        return output with
        {
            CapturedBytes = captured,
            BytesObserved = observed,
            BytesCaptured = capturedLength,
            BytesDiscarded = discarded,
            Truncated = output.Truncated || boundTruncated,
        };
    }

    private bool TryHandleHelperError(
        AppleVirtualizationLedgerEntry<ProcessInvocation, ProcessInvocationStatus> entry,
        AppleVirtualizationHelperEnvelope response,
        string operation,
        out Diagnostic? diagnostic)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            diagnostic = ProcessDiagnostics.ToDiagnostic(response.Error, operation);
            Update(entry, entry.Status with
            {
                Diagnostics = AppendDiagnostic(entry.Status.Diagnostics, diagnostic),
                LastTransitionAt = DateTimeOffset.UtcNow,
            });
            return true;
        }

        diagnostic = null;
        return false;
    }

    private static void ThrowIfHelperError(AppleVirtualizationHelperEnvelope response, string operation)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            throw ProcessDiagnostics.ToException(ProcessDiagnostics.ToDiagnostic(response.Error, operation));
        }
    }

    private sealed class ProcessOutputCapture
    {
        private readonly ProcessIoSpec _io;
        private readonly TimeSpan _outputDrainTimeout;
        private readonly StreamCapture _stdout;
        private readonly StreamCapture _stderr;
        private bool _hasEvents;

        public ProcessOutputCapture(ProcessIoSpec io, TimeSpan outputDrainTimeout)
        {
            _io = io;
            _outputDrainTimeout = outputDrainTimeout;
            _stdout = new StreamCapture(io.StandardOutput);
            _stderr = new StreamCapture(io.StandardError);
        }

        public ProcessOutputChunk Record(ProcessOutputChunk chunk)
        {
            _hasEvents = true;
            ProcessOutputStream stream = _io.MergeStandardError && chunk.Stream == ProcessOutputStream.Stderr
                ? ProcessOutputStream.Stdout
                : chunk.Stream;
            ProcessOutputChunk normalized = stream == chunk.Stream ? chunk : chunk with { Stream = stream };

            if (stream == ProcessOutputStream.Stdout)
            {
                _stdout.Record(normalized);
            }
            else
            {
                _stderr.Record(normalized);
            }

            return normalized;
        }

        public bool HasEvents => _hasEvents;

        public ProcessCapturedOutput ToOutput(bool outputDrainTimedOut) =>
            new()
            {
                Stdout = _stdout.ToOutput(),
                Stderr = _io.MergeStandardError ? new ProcessStreamOutput() : _stderr.ToOutput(),
                MergedStandardError = _io.MergeStandardError,
                OutputDrainTimedOut = outputDrainTimedOut,
                OutputDrainTimeout = _outputDrainTimeout,
            };
    }

    private sealed class StreamCapture
    {
        private readonly ProcessOutputSpec _spec;
        private readonly ArrayBufferWriter<byte>? _buffer;
        private readonly long _maxCapturedBytes;
        private long _bytesObserved;
        private long _bytesCaptured;
        private long _bytesDiscarded;
        private bool _truncated;

        public StreamCapture(ProcessOutputSpec spec)
        {
            _spec = spec;
            _maxCapturedBytes = spec.MaxCapturedBytes is null ? long.MaxValue : Math.Max(0, spec.MaxCapturedBytes.Value);
            _buffer = spec.Capture && _maxCapturedBytes > 0 ? new ArrayBufferWriter<byte>() : null;
        }

        public void Record(ProcessOutputChunk chunk)
        {
            int length = chunk.Bytes.Length;
            _bytesObserved += length;

            if ((chunk.Flags & ProcessOutputChunkFlags.Truncated) != 0)
            {
                _truncated = true;
            }

            if (!_spec.Capture)
            {
                _bytesDiscarded += length;
                return;
            }

            long remaining = _maxCapturedBytes - _bytesCaptured;
            int toCapture = remaining <= 0 ? 0 : (int)Math.Min(length, remaining);
            if (toCapture > 0 && _buffer is not null)
            {
                Span<byte> target = _buffer.GetSpan(toCapture);
                chunk.Bytes.Span.Slice(0, toCapture).CopyTo(target);
                _buffer.Advance(toCapture);
                _bytesCaptured += toCapture;
            }

            int discarded = length - toCapture;
            if (discarded > 0)
            {
                _bytesDiscarded += discarded;
                _truncated = true;
            }
        }

        public ProcessStreamOutput ToOutput() =>
            new()
            {
                CapturedBytes = _buffer?.WrittenMemory.ToArray() ?? ReadOnlyMemory<byte>.Empty,
                BytesObserved = _bytesObserved,
                BytesCaptured = _bytesCaptured,
                BytesDiscarded = _bytesDiscarded,
                Truncated = _truncated,
            };
    }
}

internal sealed class AppleVirtualizationProcessInvocationHandle : IProcessInvocationHandle
{
    private readonly AppleVirtualizationProcessProvider _provider;

    public AppleVirtualizationProcessInvocationHandle(
        AppleVirtualizationProcessProvider provider,
        TargetHandle<ProcessInvocation> handle,
        ResourceRef<ProcessInvocation> resource,
        ProcessInvocationSpec spec)
    {
        _provider = provider;
        Handle = handle;
        Resource = resource;
        Spec = spec;
    }

    public TargetHandle<ProcessInvocation> Handle { get; }
    public ResourceRef<ProcessInvocation>? Resource { get; }
    public ProcessInvocationSpec Spec { get; }

    public ValueTask WriteStdinAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        _provider.WriteStdinAsync(Handle, bytes, cancellationToken);

    public ValueTask CloseStdinAsync(CancellationToken cancellationToken = default) =>
        _provider.CloseStdinAsync(Handle, cancellationToken);

    public ValueTask SignalAsync(ProcessSignal signal, CancellationToken cancellationToken = default) =>
        _provider.SignalAsync(Handle, signal, cancellationToken);

    public ValueTask StopAsync(ProcessStopRequest request, CancellationToken cancellationToken = default) =>
        _provider.StopAsync(Handle, request, cancellationToken);

    public ValueTask ResizeTerminalAsync(TerminalSpec size, CancellationToken cancellationToken = default) =>
        _provider.ResizeTerminalAsync(Handle, size, cancellationToken);

    public ValueTask<ProcessInvocationResult> WaitAsync(CancellationToken cancellationToken = default) =>
        _provider.WaitAsync(Handle, cancellationToken);

    public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(CancellationToken cancellationToken = default) =>
        _provider.ReadOutputAsync(Handle, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class ProcessDiagnostics
{
    private static readonly DiagnosticCode HelperErrorCode = new("AppleVirtualization.ProcessHelperError");
    private static readonly DiagnosticCode UnsupportedResizeCode = new("AppleVirtualization.ProcessResizeUnsupported");
    private static readonly DiagnosticCode GuestNotReadyCode = new("AppleVirtualization.ProcessGuestNotReady");
    private static readonly DiagnosticCode ProjectionNotReadyCode = new("AppleVirtualization.ProcessProjectionNotReady");
    private static readonly DiagnosticCode AuthorityBindingNotReadyCode = new("AppleVirtualization.ProcessAuthorityBindingNotReady");
    private static readonly DiagnosticCode RunCancelledCode = new("AppleVirtualization.ProcessRunCancelled");
    private static readonly DiagnosticCode RunTimedOutCode = new("AppleVirtualization.ProcessRunTimedOut");
    private static readonly DiagnosticCode AlreadyExitedCode = new("AppleVirtualization.ProcessAlreadyExited");

    public static Diagnostic GuestNotReady(string unitId, string command) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = GuestNotReadyCode,
            Message = $"The Apple Virtualization guest agent is not ready for execution unit '{unitId}', so command '{command}' cannot start.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "process.start",
        };

    public static Diagnostic ProjectionNotReady(string projectionId, string guestPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = ProjectionNotReadyCode,
            Message = $"The Apple Virtualization projection '{projectionId}' is not guest-verified at '{guestPath}', so the process working directory cannot be used.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "process.start",
        };

    public static Diagnostic AuthorityBindingNotReady(string bindingId, string reason) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = AuthorityBindingNotReadyCode,
            Message = $"The Apple Virtualization authority binding '{bindingId}' is not usable for this process: {reason}",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "process.isolation.authorityBindings",
        };

    public static Diagnostic RunCancelled(string processId) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = RunCancelledCode,
            Message = $"The Apple Virtualization process '{processId}' run was cancelled by the caller.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "process.run",
        };

    public static Diagnostic RunTimedOut(string processId, TimeSpan? timeout) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = RunTimedOutCode,
            Message = timeout is { } value
                ? $"The Apple Virtualization process '{processId}' exceeded its timeout of {value}."
                : $"The Apple Virtualization process '{processId}' timed out.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = "process.run",
        };

    public static Diagnostic AlreadyExited(string processId, string operation) =>
        new()
        {
            Severity = DiagnosticSeverity.Warning,
            Code = AlreadyExitedCode,
            Message = $"The Apple Virtualization process '{processId}' already reached a terminal state before '{operation}'.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = operation,
        };

    public static Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, string operation)
    {
        if (error is null)
        {
            return new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = HelperErrorCode,
                Message = "The Apple Virtualization helper returned an error response without an error payload.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = operation,
            };
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = error.Message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = error.Operation ?? operation,
            Detail = error.Detail.IsEmpty || error.DetailSchema is null
                ? null
                : new ProviderExtensionData(
                    AppleVirtualizationProviderDescriptor.ProviderId,
                    error.DetailSchema.Value,
                    AppleVirtualizationHelperProtocol.JsonContentType,
                    error.Detail),
        };
    }

    public static Condition ToCondition(Diagnostic diagnostic) =>
        new(
            "AppleVirtualizationProcess",
            ConditionStatus.False,
            diagnostic.Code.Value,
            diagnostic.Message,
            DateTimeOffset.UtcNow,
            default,
            diagnostic.Severity);

    public static InvalidOperationException ToException(Diagnostic? diagnostic, string fallbackTargetPath = "process") =>
        ToException(diagnostic ?? new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = HelperErrorCode,
            Message = "The Apple Virtualization process operation failed.",
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = fallbackTargetPath,
        });

    public static InvalidOperationException ToException(Diagnostic diagnostic) =>
        new(diagnostic.Code.Value + ": " + diagnostic.Message);

    public static NotSupportedException UnsupportedResize() =>
        new(
            UnsupportedResizeCode.Value +
            ": terminal resize requires end-to-end guest-agent PTY support; the helper protocol shape exists but provider behavior remains unsupported.");
}
