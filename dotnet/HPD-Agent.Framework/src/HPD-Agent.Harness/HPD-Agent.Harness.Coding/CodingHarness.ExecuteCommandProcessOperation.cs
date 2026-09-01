using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;

internal enum ExecuteCommandProcessOperationStatus { Running, Completed, Stopped, Cancelled, TimedOut, Faulted }

/// <summary>Owns one coding process while a unified agent operation tracks its lifecycle.</summary>
internal sealed class ExecuteCommandProcessOperation : IAsyncDisposable
{
    private readonly FunctionExecutionContext _context;
    private readonly SemaphoreSlim _completionLock = new(1, 1);
    private Task<ProcessInvocationResult>? _completionTask;
    private bool _disposed;

    public ExecuteCommandProcessOperation(
        string commandId, string sessionId, ExecuteCommandRequest request, string shell,
        string baseCommand, ExecuteCommandCategory category, IProcessInvocationHandle process,
        ExecuteCommandOutputStoreSession outputStore, FunctionExecutionContext context)
    {
        CommandId = commandId; SessionId = sessionId; Request = request; Shell = shell;
        BaseCommand = baseCommand; Category = category; Process = process; OutputStore = outputStore;
        _context = context;
        Metadata = new(StringComparer.Ordinal)
        {
            ["command"] = request.Command, ["cwd"] = request.WorkingDirectory,
            ["baseCommand"] = baseCommand, ["category"] = category.ToString()
        };
    }

    public string CommandId { get; }
    public string SessionId { get; }
    public string? OperationId { get; set; }
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
    public ExecuteCommandProcessOperationStatus Status { get; private set; } = ExecuteCommandProcessOperationStatus.Running;
    public ExecuteCommandOutputStoreMetadata? OutputMetadata { get; private set; }
    public ConcurrentDictionary<string, string> Metadata { get; }

    public void SuppressFinalStateNotification(string reason)
    {
        Metadata["operation.notification.suppressed"] = "true";
        Metadata["operation.notification.suppressionReason"] = reason;
    }

    public async ValueTask<AgentOperationCompletion> ObserveCompletionAsync(CancellationToken operationToken)
    {
        using var stopRegistration = operationToken.Register(static state =>
        {
            var operation = (ExecuteCommandProcessOperation)state!;
            _ = Task.Run(async () =>
            {
                try { await operation.Process.StopAsync(new(StopKind.GracefulThenKill, "operation-cancelled"), CancellationToken.None); }
                catch { }
            });
        }, this);
        var result = await WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false);
        await PublishProcessExitedAsync(result).ConfigureAwait(false);
        operationToken.ThrowIfCancellationRequested();
        var artifacts = OutputMetadata is null ? [OutputStore.CombinedPath] : new[]
        {
            OutputMetadata.Stdout.ArtifactPath ?? OutputMetadata.Stdout.LocalPath,
            OutputMetadata.Stderr.ArtifactPath ?? OutputMetadata.Stderr.LocalPath,
            OutputMetadata.Combined.ArtifactPath ?? OutputMetadata.Combined.LocalPath
        };
        return new($"Command finished with {CompletionKind} and exit code {ExitCode?.ToString() ?? "none"}.", artifacts);
    }

    public Task<ProcessInvocationResult> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        lock (this) return _completionTask ??= CompleteOnceAsync(cancellationToken);
    }

    public async ValueTask FlushOutputAsync(CancellationToken cancellationToken)
    {
        if (Status == ExecuteCommandProcessOperationStatus.Running)
            await OutputStore.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessInvocationResult> CompleteOnceAsync(CancellationToken cancellationToken)
    {
        await _completionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await Process.WaitAsync(cancellationToken).ConfigureAwait(false);
            CompletedAt = DateTimeOffset.UtcNow; ExitCode = result.ExitCode;
            CompletionKind = CodingToolHarness.ToExecuteCommandCompletionKind(result.CompletionKind);
            Status = result.CompletionKind switch
            {
                ProcessCompletionKind.Completed => ExecuteCommandProcessOperationStatus.Completed,
                ProcessCompletionKind.Stopped => ExecuteCommandProcessOperationStatus.Stopped,
                ProcessCompletionKind.Cancelled => ExecuteCommandProcessOperationStatus.Cancelled,
                ProcessCompletionKind.TimedOut => ExecuteCommandProcessOperationStatus.TimedOut,
                _ => ExecuteCommandProcessOperationStatus.Faulted
            };
            OutputMetadata = await OutputStore.CompleteAsync(result, Shell, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { _completionLock.Release(); }
    }

    private async ValueTask PublishProcessExitedAsync(ProcessInvocationResult result)
    {
        if (OutputMetadata is null) return;
        if (OutputMetadata.ContentWriteFailure is { } failure)
        {
            await _context.TryPublishAsync(new ExecuteCommandContentWriteFailedEvent
            {
                ToolCallId = _context.FunctionCallId, FunctionName = _context.FunctionName,
                EventFlowId = CommandId, CommandId = CommandId, Command = Request.Command,
                BaseCommand = BaseCommand, Category = Category, WorkingDirectory = Request.WorkingDirectory,
                FailureKind = failure.Kind, ArtifactRole = failure.ArtifactRole, Message = failure.Message,
                StdoutTail = failure.StdoutTail, StderrTail = failure.StderrTail,
                MaxPersistedOutputBytes = OutputMetadata.MaxPersistedOutputBytes
            }, CancellationToken.None).ConfigureAwait(false);
        }
        var terminalEventPublished = await _context.TryPublishAsync(new ExecuteCommandProcessExitedEvent
        {
            ToolCallId = _context.FunctionCallId, FunctionName = _context.FunctionName,
            EventFlowId = CommandId, CommandId = CommandId, Command = Request.Command,
            BaseCommand = BaseCommand, Category = Category, WorkingDirectory = Request.WorkingDirectory,
            ExitCode = result.ExitCode, CompletionKind = CodingToolHarness.ToExecuteCommandCompletionKind(result.CompletionKind),
            DurationMilliseconds = Math.Max(0, (long)((CompletedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds),
            StdoutBytes = result.Output.Stdout.BytesObserved, StderrBytes = result.Output.Stderr.BytesObserved,
            CombinedOutputBytes = result.Output.Stdout.BytesObserved + result.Output.Stderr.BytesObserved,
            StdoutBytesDiscarded = result.Output.Stdout.BytesDiscarded, StderrBytesDiscarded = result.Output.Stderr.BytesDiscarded,
            CombinedBytesDiscarded = result.Output.Stdout.BytesDiscarded + result.Output.Stderr.BytesDiscarded,
            OutputTruncated = result.Output.Stdout.Truncated || result.Output.Stderr.Truncated || OutputMetadata.Stdout.Truncated || OutputMetadata.Stderr.Truncated,
            OutputDrainTimedOut = result.Output.OutputDrainTimedOut, OutputEventsSuppressed = false,
            OutputContentState = OutputMetadata.ContentState,
            Stdout = OutputMetadata.Stdout.Address,
            Stderr = OutputMetadata.Stderr.Address,
            CombinedOutput = OutputMetadata.Combined.Address,
            MaxPersistedOutputBytes = OutputMetadata.MaxPersistedOutputBytes,
            CombinedOutputFormat = "hpd.execute-command.interleaved.v1"
        }, CancellationToken.None).ConfigureAwait(false);
        if (terminalEventPublished)
            await OutputStore.MarkCommittedAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await OutputStore.DisposeAsync().ConfigureAwait(false);
        await Process.DisposeAsync().ConfigureAwait(false);
        _completionLock.Dispose();
    }
}
