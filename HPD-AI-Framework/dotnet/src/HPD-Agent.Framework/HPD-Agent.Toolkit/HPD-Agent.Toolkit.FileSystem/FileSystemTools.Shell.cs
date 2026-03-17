using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace HPD.Agent.Toolkit.FileSystem;

/// <summary>
/// Shell command execution functionality for FileSystemTools
/// </summary>
public partial class FileSystemTools
{
    #region Shell Operations

    [AIFunction<FileSystemContext>]
    [ConditionalFunction("EnableShell")]
    [RequiresPermission]
    [AIDescription("Execute a shell command.")]
    public async Task<string> ExecuteShellCommand(
        [AIDescription("The shell command to execute")] string command,
        [AIDescription("Optional: Working directory (defaults to workspace root)")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        //  LAYER 1: Check if shell is enabled
        if (!_context.EnableShell)
            return "Error: Shell command execution is disabled in this context.";

        //  LAYER 2: Set timeout from context (internal, not exposed to AI)
        var timeout = _context.MaxShellTimeoutSeconds;

        //  LAYER 3: Validate command is allowed
        var rootCommand = GetRootCommand(command);
        if (!IsCommandAllowed(rootCommand))
        {
            return $"Error: Command '{rootCommand}' is not allowed. " +
                   $"Blocked commands include: {string.Join(", ", _context.BlockedShellCommands)}";
        }

        //  LAYER 4: Validate working directory
        var workDir = workingDirectory ?? _context.WorkspaceRoot;

        if (!Path.IsPathRooted(workDir))
            workDir = Path.Combine(_context.WorkspaceRoot, workDir);

        if (!_context.IsPathWithinWorkspace(workDir))
            return $"Error: Working directory must be within workspace: {_context.WorkspaceRoot}";

        if (!Directory.Exists(workDir))
            return $"Error: Working directory not found: {workDir}";

        //  LAYER 5: Execute with CliWrap (safe, controlled execution)
        try
        {
            var (shell, shellArgs) = GetShellExecutable();

            // Use CliWrap's argument builder for proper escaping
            var result = await Cli.Wrap(shell)
                .WithArguments(args => args
                    .Add(shellArgs)
                    .Add(command)) // CliWrap handles escaping
                .WithWorkingDirectory(workDir)
                .WithValidation(CommandResultValidation.None) // Don't throw on non-zero exit
                .ExecuteBufferedAsync(
                    cancellationToken: CreateTimeoutToken(timeout, cancellationToken));

            return FormatShellOutput(command, result, workDir);
        }
        catch (OperationCanceledException)
        {
            return $"Error: Command timed out after {timeout} seconds.\nCommand: {command}";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}\nCommand: {command}";
        }
    }

    #endregion

    #region Shell Helper Methods

    /// <summary>
    /// Extracts the root command from a shell command string
    /// </summary>
    private static string GetRootCommand(string command)
    {
        var trimmed = command.Trim();

        // Handle shell redirects and pipes
        var firstSpecialChar = trimmed.IndexOfAny(new[] { ' ', '|', '>', '<', ';', '&' });
        var rootCmd = firstSpecialChar > 0 ? trimmed[..firstSpecialChar] : trimmed;

        // Extract just the command name from path (e.g., /usr/bin/git → git)
        return Path.GetFileName(rootCmd).ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a command is allowed based on allowlist and blocklist
    /// </summary>
    private bool IsCommandAllowed(string rootCommand)
    {
        // Blocklist takes precedence - always deny blocked commands
        if (_context.BlockedShellCommands.Contains(rootCommand))
            return false;

        // If allowlist is empty, allow all (except blocked)
        if (_context.AllowedShellCommands.Count == 0)
            return true;

        // Otherwise, must be in allowlist
        return _context.AllowedShellCommands.Contains(rootCommand);
    }

    /// <summary>
    /// Gets the shell executable and base arguments for the current platform
    /// </summary>
    private static (string shell, string args) GetShellExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", "/c");
        }
        else
        {
            // Unix-like systems (Linux, macOS)
            return ("/bin/sh", "-c");
        }
    }

    /// <summary>
    /// Escapes shell arguments to prevent injection
    /// </summary>
    private static string EscapeShellArgument(string argument)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows cmd.exe escaping
            return $"\"{argument.Replace("\"", "\\\"")}\"";
        }
        else
        {
            // Unix shell escaping - wrap in single quotes and escape existing single quotes
            return $"'{argument.Replace("'", "'\\''")}'";
        }
    }

    /// <summary>
    /// Formats shell command output for display
    /// </summary>
    private static string FormatShellOutput(string command, BufferedCommandResult result, string workDir)
    {
        var output = new StringBuilder();

        output.AppendLine($"Command: {command}");
        output.AppendLine($"Working Directory: {workDir}");
        output.AppendLine($"Exit Code: {result.ExitCode}");
        output.AppendLine($"Duration: {result.RunTime.TotalSeconds:F2}s");
        output.AppendLine();

        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            output.AppendLine("=== STDOUT ===");
            output.AppendLine(result.StandardOutput.TrimEnd());
            output.AppendLine();
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            output.AppendLine("=== STDERR ===");
            output.AppendLine(result.StandardError.TrimEnd());
            output.AppendLine();
        }

        if (result.ExitCode != 0)
        {
            output.AppendLine($"  Command failed with exit code {result.ExitCode}");
        }
        else
        {
            output.AppendLine("✓ Command completed successfully");
        }

        return output.ToString();
    }

    /// <summary>
    /// Creates a combined cancellation token with timeout
    /// </summary>
    private static CancellationToken CreateTimeoutToken(int timeoutSeconds, CancellationToken userToken)
    {
        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        if (userToken == default || !userToken.CanBeCanceled)
            return timeoutCts.Token;

        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, userToken);
        return combinedCts.Token;
    }

    #endregion
}
