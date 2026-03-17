using System.Threading.Channels;
using HPD.Agent.Sandbox;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Platforms;

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
/// <para><b>Behavior:</b></para>
/// <para>
/// Based on <see cref="SandboxConfig.OnInitializationFailure"/>:
/// <list type="bullet">
/// <item><c>Block</c>: Throws <see cref="PlatformNotSupportedException"/></item>
/// <item><c>Warn</c>: Logs warning and runs commands unsandboxed</item>
/// <item><c>Ignore</c>: Silently runs commands unsandboxed</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class WindowsSandbox : IPlatformSandbox
{
    private readonly SandboxConfig _config;
    private readonly ILogger? _logger;
    private bool _warningLogged;

    public WindowsSandbox(
        SandboxConfig config,
        ILogger? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public ChannelReader<SandboxViolation>? Violations => null;

    public Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        // Windows sandboxing dependencies are never available
        // Return false to indicate sandbox cannot be initialized
        return Task.FromResult(false);
    }

    public Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken)
    {
        // Handle based on failure behavior
        switch (_config.OnInitializationFailure)
        {
            case SandboxFailureBehavior.Block:
                throw new PlatformNotSupportedException(
                    "OS-level sandboxing is not supported on Windows. " +
                    "Consider using HPD.Sandbox.Container with Docker Desktop, " +
                    "or WSL2 with the Linux sandbox. " +
                    "Set OnInitializationFailure to Warn or Ignore to run unsandboxed.");

            case SandboxFailureBehavior.Warn:
                if (!_warningLogged)
                {
                    _logger?.LogWarning(
                        "Windows sandboxing not supported. Running command unsandboxed: {Command}",
                        TruncateForLog(command));
                    _warningLogged = true;
                }
                return Task.FromResult(command);

            case SandboxFailureBehavior.Ignore:
            default:
                return Task.FromResult(command);
        }
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
