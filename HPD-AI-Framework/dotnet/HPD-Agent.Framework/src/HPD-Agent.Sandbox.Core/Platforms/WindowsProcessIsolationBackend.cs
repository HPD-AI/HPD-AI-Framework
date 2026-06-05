using System.Threading.Channels;
using HPD.Execution.Contracts;
using HPD.Agent.Sandbox.ProcessIsolation;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Sandbox.Platforms;

/// <summary>
/// Windows sandbox stub - OS-level sandboxing is not currently supported on Windows.
/// </summary>
/// <remarks>
/// <para><b>Why Windows is Unsupported:</b></para>
/// <list type="bullet">
/// <item>Windows lacks a lightweight containerization tool like bwrap or sandbox-exec</item>
/// <item>Windows Sandbox requires Hyper-V and is too heavyweight for per-function isolation</item>
/// <item>AppContainer requires app manifest changes and can't wrap arbitrary commands</item>
/// <item>Job Objects provide process limits but not filesystem/network isolation</item>
/// </list>
///
/// <para><b>Alternatives for Windows Users:</b></para>
/// <list type="bullet">
/// <item>Use HPD.Sandbox.Container with Docker Desktop</item>
/// <item>Use WSL2 with the Linux sandbox</item>
/// <item>Run in a Windows Sandbox VM manually</item>
/// </list>
///
/// </remarks>
internal sealed class WindowsProcessIsolationBackend : ISandboxBackend
{
    private readonly SandboxIsolationPlan _plan;
    private readonly ILogger? _logger;
    private bool _warningLogged;

    public WindowsProcessIsolationBackend(
        SandboxIsolationPlan plan,
        ILogger? logger = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _logger = logger;
    }

    public ChannelReader<ProcessIsolationViolation>? Violations => null;

    public async Task<PreparedSandboxCommand> WrapCommandAsync(CommandInvocation command, CancellationToken cancellationToken)
    {
        _ = await WrapCommandAsync(
            HPD.Agent.Sandbox.Security.PosixShellQuoter.RenderCommand(command),
            cancellationToken);

        return new PreparedSandboxCommand(command.FileName, command.ArgumentList);
    }

    public Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation command,
        SandboxIsolationPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return WrapCommandAsync(command, cancellationToken);
    }

    public Task<ProcessIsolationDependencyCheck> GetDependencyCheckAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessIsolationDependencyCheck
        {
            Errors =
            [
                "OS-level sandboxing is not supported on Windows. " +
                "Use HPD.Sandbox.Container with Docker Desktop or WSL2 with the Linux sandbox."
            ],
        });
    }

    public async Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken) =>
        (await GetDependencyCheckAsync(cancellationToken)).IsAvailable;

    public Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (_plan.Degradation.Mode is ProcessIsolationDegradationMode.FailClosed)
        {
            throw new PlatformNotSupportedException(
                "OS-level process isolation is not supported on Windows. " +
                "Use a runtime-host provider, WSL2 with the Linux backend, or explicitly allow degraded execution.");
        }

        if (!_warningLogged)
        {
            _logger?.LogWarning(
                "Windows process isolation is degraded. Running command without OS isolation: {Command}",
                TruncateForLog(command));
            _warningLogged = true;
        }

        return Task.FromResult(command);
    }

    /// <summary>
    /// Truncates command for logging to avoid exposing sensitive data.
    /// </summary>
    private static string TruncateForLog(string command)
    {
        const int maxLength = 100;
        if (command.Length <= maxLength)
            return command;
        return command[..maxLength] + "...";
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
