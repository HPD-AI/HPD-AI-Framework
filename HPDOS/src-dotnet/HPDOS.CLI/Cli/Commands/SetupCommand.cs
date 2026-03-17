using Spectre.Console;
using System.Runtime.InteropServices;

namespace HPDOS.Shell.Cli.Commands;

public static class SetupCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            AnsiConsole.MarkupLine("[bold]HPDOS CLI Setup[/]");
            AnsiConsole.MarkupLine("This will register [cyan]hpdos[/] to your PATH.\n");

            // Get current binary location
            var binaryPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine binary path");

            // Determine OS and installation path
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SetupWindows(binaryPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await SetupUnix(binaryPath);
            }

            AnsiConsole.MarkupLine("[red]Unsupported OS[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Setup failed:[/] {ex.Message}");
            return 1;
        }
    }

    private static int SetupWindows(string binaryPath)
    {
        try
        {
            var programFilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "HPDOS"
            );

            AnsiConsole.MarkupLine($"Installing to: [cyan]{programFilesPath}[/]\n");

            // Create directory
            Directory.CreateDirectory(programFilesPath);

            // Copy binary
            var targetPath = Path.Combine(programFilesPath, "hpdos.exe");
            File.Copy(binaryPath, targetPath, overwrite: true);
            AnsiConsole.MarkupLine("[green]Copied binary[/]");

            // Add to PATH via registry
            AddToWindowsPath(programFilesPath);
            AnsiConsole.MarkupLine("[green]Added to Windows PATH[/]");

            AnsiConsole.MarkupLine("\n[green]Setup complete![/]");
            AnsiConsole.MarkupLine("Please restart your terminal or run: [cyan]set PATH=%PATH%;{0}[/]", programFilesPath);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Windows setup failed:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> SetupUnix(string binaryPath)
    {
        try
        {
            const string installDir = "/usr/local/bin";
            const string binaryName = "hpdos";
            var targetPath = Path.Combine(installDir, binaryName);

            AnsiConsole.MarkupLine($"Installing to: [cyan]{targetPath}[/]\n");

            // Check if we need sudo
            var needsSudo = !IsWritableDirectory(installDir);

            if (needsSudo)
            {
                AnsiConsole.MarkupLine("[yellow]This requires sudo permission[/]");
                AnsiConsole.MarkupLine("You will be prompted for your password.\n");

                // Use sudo to copy
                var result = await ExecuteCommandAsync("sudo", $"cp {binaryPath} {targetPath}");
                if (result != 0)
                {
                    AnsiConsole.MarkupLine("[red]Failed to copy binary[/]");
                    return 1;
                }

                // Make executable
                result = await ExecuteCommandAsync("sudo", $"chmod +x {targetPath}");
                if (result != 0)
                {
                    AnsiConsole.MarkupLine("[red]Failed to make executable[/]");
                    return 1;
                }
            }
            else
            {
                // Can write without sudo
                File.Copy(binaryPath, targetPath, overwrite: true);
                File.SetAttributes(targetPath,
                    File.GetAttributes(targetPath) | FileAttributes.UserExecute);
            }

            AnsiConsole.MarkupLine("[green]Installed successfully[/]");

            // Verify
            var testResult = await ExecuteCommandAsync("which", binaryName);
            if (testResult == 0)
            {
                AnsiConsole.MarkupLine("\n[green]Setup complete![/]");
                AnsiConsole.MarkupLine("Run [cyan]hpdos --help[/] to get started");
                return 0;
            }

            AnsiConsole.MarkupLine("\n[yellow]Binary installed but not yet in PATH[/]");
            AnsiConsole.MarkupLine("Please open a new terminal or run: [cyan]source ~/.bashrc[/] (or ~/.zshrc)");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unix setup failed:[/] {ex.Message}");
            return 1;
        }
    }

    private static bool IsWritableDirectory(string path)
    {
        try
        {
            var testFile = Path.Combine(path, ".hpdos-test-" + Guid.NewGuid());
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> ExecuteCommandAsync(string command, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {command}");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static void AddToWindowsPath(string pathToAdd)
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Environment", writable: true)
                ?? throw new InvalidOperationException("Could not open registry");

            var currentPath = (key.GetValue("Path") as string) ?? "";
            if (!currentPath.Contains(pathToAdd))
            {
                var newPath = string.IsNullOrEmpty(currentPath)
                    ? pathToAdd
                    : currentPath + ";" + pathToAdd;
                key.SetValue("Path", newPath);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Could not update registry: {ex.Message}");
            AnsiConsole.MarkupLine("You may need to manually add to PATH");
        }
    }
}
