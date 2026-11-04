namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.Threading.Channels;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.ProcessIsolation;
using Microsoft.Extensions.Logging;

/// <summary>
/// Windows Host Process execution without OS confinement. The backend is
/// available only when the caller explicitly permits provider degradation;
/// otherwise dependency validation fails closed.
/// </summary>
internal sealed class WindowsProcessIsolationBackend(
    SandboxIsolationPlan plan,
    ILogger? logger = null)
    : ISandboxBackend
{
    private bool _warningLogged;

    public ChannelReader<ProcessIsolationViolation>? Violations =>
        null;

    public Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireDegradedExecution();
        LogDegradation();
        return Task.FromResult(
            new PreparedSandboxCommand(
                command.FileName,
                command.ArgumentList));
    }

    public Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation command,
        SandboxIsolationPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return WrapCommandAsync(command, cancellationToken);
    }

    public Task<string> WrapCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireDegradedExecution();
        LogDegradation();
        return Task.FromResult(command);
    }

    public Task<ProcessIsolationDependencyCheck>
        GetDependencyCheckAsync(
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AllowsDegradedExecution(plan))
        {
            return Task.FromResult(
                ProcessIsolationDependencyCheck.FromIssues(
                [
                    ProcessIsolationDependencyIssue.Error(
                        "windows.isolation.unsupported",
                        "windows-host-process",
                        "Windows Host Process has no accepted OS confinement backend. Select Host Containers or explicitly allow provider degradation."),
                ]));
        }
        return Task.FromResult(
            ProcessIsolationDependencyCheck.FromIssues(
            [
                ProcessIsolationDependencyIssue.Warning(
                    "windows.isolation.degraded",
                    "windows-host-process",
                    "Windows Host Process runs without OS filesystem, network, or process-tree confinement."),
            ]));
    }

    public async Task<bool> CheckDependenciesAsync(
        CancellationToken cancellationToken) =>
        (await GetDependencyCheckAsync(cancellationToken)
            .ConfigureAwait(false)).IsAvailable;

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;

    private static bool AllowsDegradedExecution(
        SandboxIsolationPlan plan) =>
        plan.Degradation.Mode is
            ProcessIsolationDegradationMode.AllowProviderDegraded or
            ProcessIsolationDegradationMode.ObserveOnly;

    private void RequireDegradedExecution()
    {
        if (!AllowsDegradedExecution(plan))
        {
            throw new PlatformNotSupportedException(
                "Windows Host Process confinement is unavailable and provider degradation was not explicitly allowed. Select Host Containers or set the degradation policy.");
        }
    }

    private void LogDegradation()
    {
        if (_warningLogged)
        {
            return;
        }
        _warningLogged = true;
        logger?.LogWarning(
            "Windows Host Process execution is degraded and has no OS-level filesystem or network confinement.");
    }
}
