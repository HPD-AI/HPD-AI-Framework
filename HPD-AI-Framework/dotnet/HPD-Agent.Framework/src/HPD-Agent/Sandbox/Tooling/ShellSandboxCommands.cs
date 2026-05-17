using HPD.Agent.Sandbox;

namespace HPD.Agent.Sandbox.Tooling;

/// <summary>
/// Tool-facing helpers for creating sandboxed process commands that intentionally
/// run through a shell.
/// </summary>
public static class ShellSandboxCommands
{
    public static SandboxedProcessCommand Posix(
        string command,
        string shell = "/bin/sh",
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(shell);

        return SandboxedProcessCommand.Exec(
            shell,
            ["-lc", command],
            workingDirectory,
            environment);
    }

    public static SandboxedProcessCommand Bash(
        string command,
        string shell = "/bin/bash",
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        Posix(command, shell, workingDirectory, environment);

    public static SandboxedProcessCommand Zsh(
        string command,
        string shell = "/bin/zsh",
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        Posix(command, shell, workingDirectory, environment);

    public static SandboxedProcessCommand WindowsCmd(
        string command,
        string shell = "cmd.exe",
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(shell);

        return SandboxedProcessCommand.Exec(
            shell,
            ["/c", command],
            workingDirectory,
            environment);
    }

    public static SandboxedProcessCommand PowerShell(
        string command,
        string shell = "pwsh",
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(shell);

        return SandboxedProcessCommand.Exec(
            shell,
            ["-NoProfile", "-NonInteractive", "-Command", command],
            workingDirectory,
            environment);
    }

    public static SandboxedProcessCommand PlatformDefault(
        string command,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        OperatingSystem.IsWindows()
            ? WindowsCmd(command, workingDirectory: workingDirectory, environment: environment)
            : Posix(command, workingDirectory: workingDirectory, environment: environment);
}
