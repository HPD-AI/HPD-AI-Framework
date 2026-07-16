using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Events;

internal enum ExecuteCommandProcessHandleStatus
{
    Running,
    Completed,
    Stopped,
    Cancelled,
    TimedOut,
    Faulted
}

internal sealed class ExecuteCommandProcessHandle :
    IReadableBackgroundHandle,
    IStoppableBackgroundHandle,
    IArtifactBackgroundHandle,
    IAsyncDisposable
{
    private readonly FunctionInvocationSnapshot _invocation;
    private readonly SemaphoreSlim _completionLock = new(1, 1);
    private Task<ProcessInvocationResult>? _completionTask;
    private bool _disposed;

    public ExecuteCommandProcessHandle(
        string commandId,
        string sessionId,
        ExecuteCommandRequest request,
        string shell,
        string baseCommand,
        ExecuteCommandCategory category,
        IProcessInvocationHandle process,
        ExecuteCommandOutputStoreSession outputStore,
        FunctionInvocationSnapshot invocation)
    {
        CommandId = commandId;
        SessionId = sessionId;
        Request = request;
        Shell = shell;
        BaseCommand = baseCommand;
        Category = category;
        Process = process;
        OutputStore = outputStore;
        _invocation = invocation;
        NotificationMetadata = new ConcurrentDictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = Request.Command,
            ["cwd"] = Request.WorkingDirectory,
            ["baseCommand"] = BaseCommand,
            ["category"] = Category.ToString()
        };
    }

    public string CommandId { get; }
    public string SessionId { get; }
    public ExecuteCommandRequest Request { get; }
    public string Shell { get; }
    public string BaseCommand { get; }
    public ExecuteCommandCategory Category { get; }
    public IProcessInvocationHandle Process { get; }
    public ExecuteCommandOutputStoreSession OutputStore { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }
    public int? ExitCode { get; private set; }
    public ExecuteCommandCompletionKind? CompletionKind { get; private set; }
    public ExecuteCommandProcessHandleStatus Status { get; private set; } = ExecuteCommandProcessHandleStatus.Running;
    public ExecuteCommandOutputStoreMetadata? OutputMetadata { get; private set; }
    public ConcurrentDictionary<string, string> NotificationMetadata { get; }

    public ValueTask<BackgroundHandleSnapshot> GetStatusAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(CreateSnapshot());

    public async ValueTask<BackgroundHandleReadResult> ReadAsync(
        BackgroundHandleReadRequest request,
        CancellationToken cancellationToken)
    {
        await FlushOutputAsync(cancellationToken).ConfigureAwait(false);
        var tail = await OutputStore.ReadCombinedTailAsync(
                request.TailLines ?? 200,
                cancellationToken)
            .ConfigureAwait(false);

        return new BackgroundHandleReadResult
        {
            Snapshot = CreateSnapshot(),
            Text = tail,
            Artifacts = CreateArtifacts()
        };
    }

    public async ValueTask<BackgroundHandleStopResult> StopAsync(
        BackgroundHandleStopRequest request,
        CancellationToken cancellationToken)
    {
        SuppressFinalStateNotification(request.Reason ?? "handled-by-foreground-stop");

        if (Status == ExecuteCommandProcessHandleStatus.Running)
        {
            await Process.StopAsync(
                new ProcessStopRequest(StopKind.GracefulThenKill, request.Reason ?? "requested"),
                cancellationToken).ConfigureAwait(false);
        }

        var result = await WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
        return new BackgroundHandleStopResult
        {
            Snapshot = CreateSnapshot(),
            CompletionKind = CodingToolHarness.ToExecuteCommandCompletionKind(result.CompletionKind).ToString(),
            ExitCode = result.ExitCode,
            Artifacts = CreateArtifacts()
        };
    }

    public ValueTask<BackgroundHandleArtifactResult> GetArtifactsAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(new BackgroundHandleArtifactResult
        {
            Snapshot = CreateSnapshot(),
            Artifacts = CreateArtifacts()
        });

    public async ValueTask DisposeAsync()
        => await DisposeOwnedResourcesAsync().ConfigureAwait(false);

    public void SuppressFinalStateNotification(string reason)
    {
        NotificationMetadata[BackgroundTaskNotificationMetadataKeys.SuppressNotification] = "true";
        NotificationMetadata[BackgroundTaskNotificationMetadataKeys.SuppressNotificationReason] = reason;
    }

    public async Task ObserveCompletionAsync(
        BackgroundTaskContext backgroundContext,
        CancellationToken runtimeToken)
    {
        using var stopRegistration = runtimeToken.Register(static state =>
        {
            var process = (ExecuteCommandProcessHandle)state!;
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.Process.StopAsync(
                            new ProcessStopRequest(StopKind.GracefulThenKill, "runtime-stopping"),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Runtime shutdown stop is best effort; completion will surface final process state.
                }
            });
        }, this);

        if (runtimeToken.IsCancellationRequested)
            await Process.StopAsync(
                new ProcessStopRequest(StopKind.GracefulThenKill, "runtime-stopping"),
                CancellationToken.None).ConfigureAwait(false);

        var result = await WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false);
        await PublishProcessExitedAsync(backgroundContext, result).ConfigureAwait(false);
        await PublishStatusChangedAsync(backgroundContext).ConfigureAwait(false);

        if (runtimeToken.IsCancellationRequested)
            throw new OperationCanceledException(runtimeToken);
    }

    public Task<ProcessInvocationResult> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        lock (this)
        {
            _completionTask ??= CompleteOnceAsync(cancellationToken);
            return _completionTask;
        }
    }

    public async ValueTask FlushOutputAsync(CancellationToken cancellationToken)
    {
        if (Status == ExecuteCommandProcessHandleStatus.Running)
            await OutputStore.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessInvocationResult> CompleteOnceAsync(CancellationToken cancellationToken)
    {
        await _completionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await Process.WaitAsync(cancellationToken).ConfigureAwait(false);
            CompletedAt = DateTimeOffset.UtcNow;
            ExitCode = result.ExitCode;
            CompletionKind = CodingToolHarness.ToExecuteCommandCompletionKind(result.CompletionKind);
            Status = ToBackgroundStatus(result.CompletionKind);
            OutputMetadata = await OutputStore.CompleteAsync(result, Shell, cancellationToken).ConfigureAwait(false);
            await DisposeOwnedResourcesAsync().ConfigureAwait(false);
            return result;
        }
        finally
        {
            _completionLock.Release();
        }
    }

    private async Task DisposeOwnedResourcesAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await OutputStore.DisposeAsync().ConfigureAwait(false);
        await Process.DisposeAsync().ConfigureAwait(false);
    }

    private BackgroundHandleSnapshot CreateSnapshot()
        => new()
        {
            HandleId = CommandId,
            Name = "ExecuteCommand",
            Kind = BackgroundHandleKind.Process,
            SourceKind = BackgroundTaskSourceKind.Command,
            Status = Status.ToString().ToLowerInvariant(),
            SourceId = CommandId,
            SessionId = SessionId,
            ThreadId = _invocation.ThreadId,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            Metadata = NotificationMetadata,
            Artifacts = CreateArtifacts()
        };

    private IReadOnlyList<BackgroundHandleArtifact> CreateArtifacts()
    {
        if (OutputMetadata is null)
        {
            return
            [
                new BackgroundHandleArtifact
                {
                    Kind = "combined_output",
                    Path = OutputStore.CombinedPath
                }
            ];
        }

        return
        [
            CreateArtifact("stdout", OutputMetadata.Stdout),
            CreateArtifact("stderr", OutputMetadata.Stderr),
            CreateArtifact("combined_output", OutputMetadata.Combined)
        ];
    }

    private static BackgroundHandleArtifact CreateArtifact(
        string kind,
        ExecuteCommandOutputHandle output)
        => new()
        {
            Kind = kind,
            Path = output.ArtifactPath ?? output.LocalPath,
            ContentId = output.ContentId
        };

    private async ValueTask PublishProcessExitedAsync(
        BackgroundTaskContext backgroundContext,
        ProcessInvocationResult result)
    {
        if (OutputMetadata is null)
            return;

        await backgroundContext.PublishAsync(new ExecuteCommandProcessExitedEvent
        {
            ToolCallId = _invocation.FunctionCallId,
            FunctionName = _invocation.FunctionName,
            SessionId = _invocation.SessionId,
            ThreadId = _invocation.ThreadId,
            TraceId = _invocation.TraceId,
            EventFlowId = CommandId,
            CommandId = CommandId,
            Command = Request.Command,
            BaseCommand = BaseCommand,
            Category = Category,
            WorkingDirectory = Request.WorkingDirectory,
            ExitCode = result.ExitCode,
            CompletionKind = CodingToolHarness.ToExecuteCommandCompletionKind(result.CompletionKind),
            DurationMilliseconds = Math.Max(0, (long)((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
            StdoutBytes = result.Output.Stdout.BytesObserved,
            StderrBytes = result.Output.Stderr.BytesObserved,
            CombinedOutputBytes = result.Output.Stdout.BytesObserved + result.Output.Stderr.BytesObserved,
            StdoutBytesDiscarded = result.Output.Stdout.BytesDiscarded,
            StderrBytesDiscarded = result.Output.Stderr.BytesDiscarded,
            CombinedBytesDiscarded = result.Output.Stdout.BytesDiscarded + result.Output.Stderr.BytesDiscarded,
            OutputTruncated = result.Output.Stdout.Truncated || result.Output.Stderr.Truncated || OutputMetadata.Stdout.Truncated || OutputMetadata.Stderr.Truncated,
            OutputDrainTimedOut = result.Output.OutputDrainTimedOut,
            OutputEventsSuppressed = false,
            StdoutArtifactPath = OutputMetadata.Stdout.ArtifactPath,
            StderrArtifactPath = OutputMetadata.Stderr.ArtifactPath,
            CombinedOutputArtifactPath = OutputMetadata.Combined.ArtifactPath,
            StdoutContentId = OutputMetadata.Stdout.ContentId,
            StderrContentId = OutputMetadata.Stderr.ContentId,
            CombinedOutputContentId = OutputMetadata.Combined.ContentId,
            StdoutLocalPath = OutputMetadata.Stdout.LocalPath,
            StderrLocalPath = OutputMetadata.Stderr.LocalPath,
            CombinedOutputLocalPath = OutputMetadata.Combined.LocalPath
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask PublishStatusChangedAsync(BackgroundTaskContext backgroundContext)
    {
        await backgroundContext.PublishAsync(new BackgroundHandleStatusChangedEvent
        {
            HandleId = CommandId,
            Status = Status.ToString().ToLowerInvariant(),
            SessionId = _invocation.SessionId,
            ThreadId = _invocation.ThreadId,
            TraceId = _invocation.TraceId,
            Metadata = NotificationMetadata,
            ObservedAt = CompletedAt ?? DateTimeOffset.UtcNow
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static ExecuteCommandProcessHandleStatus ToBackgroundStatus(ProcessCompletionKind completionKind)
        => completionKind switch
        {
            ProcessCompletionKind.Completed => ExecuteCommandProcessHandleStatus.Completed,
            ProcessCompletionKind.Stopped => ExecuteCommandProcessHandleStatus.Stopped,
            ProcessCompletionKind.Cancelled => ExecuteCommandProcessHandleStatus.Cancelled,
            ProcessCompletionKind.TimedOut => ExecuteCommandProcessHandleStatus.TimedOut,
            ProcessCompletionKind.Exited => ExecuteCommandProcessHandleStatus.Faulted,
            _ => ExecuteCommandProcessHandleStatus.Faulted
        };
}
