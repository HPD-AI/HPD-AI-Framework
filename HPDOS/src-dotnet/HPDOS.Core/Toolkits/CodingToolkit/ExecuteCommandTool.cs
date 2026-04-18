using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HPD.Agent;

/// <summary>
/// ExecuteCommand implementation for CodingToolkit (partial class).
/// Cross-platform shell execution with timeout support.
/// </summary>
public partial class CodingToolkit
{
    [AIFunction]
    [AIDescription("Execute a shell command and return its output. Use for build, test, package management, and running scripts.")]
    public async Task<string> ExecuteCommand(
        [AIDescription("Command to execute (e.g., 'dotnet build', 'npm test', 'git status')")] string command,
        [AIDescription("Working directory for command execution. Default: current directory")] string workingDirectory = "",
        [AIDescription("Timeout in milliseconds. Default: 120000 (2 minutes)")] int timeout = 120000)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: Command cannot be empty";

        // Use current directory if not specified
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory;

        if (!Directory.Exists(workDir))
            return $"Error: Working directory not found: {workDir}";

        try
        {
            var (shell, shellArg) = GetShellExecutable();

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"{shellArg} \"{command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) error.AppendLine(e.Data);
            };

            var sw = Stopwatch.StartNew();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeout));
            sw.Stop();

            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { }

                return FormatCommandResult(
                    command: command,
                    workingDir: workDir,
                    exitCode: -1,
                    output: output.ToString(),
                    error: $"Command timed out after {timeout}ms",
                    duration: sw.ElapsedMilliseconds,
                    timedOut: true
                );
            }

            return FormatCommandResult(
                command: command,
                workingDir: workDir,
                exitCode: process.ExitCode,
                output: output.ToString(),
                error: error.ToString(),
                duration: sw.ElapsedMilliseconds,
                timedOut: false
            );
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}\n" +
                   $"Command: {command}\n" +
                   $"Working Directory: {workDir}";
        }
    }
}
