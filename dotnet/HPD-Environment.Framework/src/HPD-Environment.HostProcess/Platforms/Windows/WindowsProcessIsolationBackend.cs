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
    private const string NetworkUnavailable =
        "Windows Host Process cannot enforce blocked or filtered network egress. Use a provider that enforces the requested network policy.";

    private bool _warningLogged;

    public ChannelReader<ProcessIsolationViolation>? Violations =>
        null;

    public Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CanUseAppContainer(plan))
            return Task.FromResult(new PreparedSandboxCommand(command.FileName, command.ArgumentList)
            {
                UseWindowsAppContainer = true,
            });
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
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanUseAppContainer(plan))
            RequireDegradedExecution(plan);
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
        if (CanUseAppContainer(plan))
            return Task.FromResult(ProcessIsolationDependencyCheck.FromIssues([]));
        if (RequiresNetworkEnforcement(plan))
        {
            return Task.FromResult(ProcessIsolationDependencyCheck.FromIssues(
            [
                ProcessIsolationDependencyIssue.Error(
                    "windows.network-enforcement.unavailable",
                    "windows-host-process",
                    NetworkUnavailable),
            ]));
        }

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

    private static bool RequiresNetworkEnforcement(SandboxIsolationPlan plan) =>
        plan.Network.Mode != NetworkEgressMode.Unrestricted;

    internal static bool CanUseAppContainer(SandboxIsolationPlan plan) =>
        OperatingSystem.IsWindows() &&
        plan.Network.Mode == NetworkEgressMode.Blocked &&
        plan.Filesystem.Rules.Count == 0 &&
        plan.AuthorityBindings.Count == 0 &&
        plan.Tls.Mode == TlsTrustMode.None &&
        plan.Violations.Action == ProcessViolationAction.ObserveOnly &&
        !plan.Interactive.AllowPty &&
        !plan.Interactive.AllowLocalBinding &&
        !plan.UnixSockets.AllowAll &&
        plan.UnixSockets.AllowedSockets.Count == 0;

    private void RequireDegradedExecution() => RequireDegradedExecution(plan);

    private static void RequireDegradedExecution(SandboxIsolationPlan plan)
    {
        if (RequiresNetworkEnforcement(plan))
            throw new PlatformNotSupportedException(NetworkUnavailable);

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
