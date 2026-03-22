using HPDOS.Shell.Cli.TUI;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.IO;

namespace HPDOS.Shell.Cli.Commands;

public static class SetupCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var session = SpectreConsoleSession.CreateDefault();

        try
        {
            session.MarkupLine("[bold]HPDOS CLI Setup[/]");
            session.MarkupLine("This will register [cyan]hpdos[/] to your PATH.\n");

            // Get current binary location
            var binaryPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine binary path");

            // Determine OS and installation path
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SetupWindows(session, binaryPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await SetupUnix(session, binaryPath);
            }

            session.MarkupLine("[red]Unsupported OS[/]");
            return 1;
        }
        catch (Exception ex)
        {
            session.MarkupLine($"[red]Setup failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static int SetupWindows(IConsoleSession session, string binaryPath)
    {
        try
        {
            var programFilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "HPDOS"
            );

            session.MarkupLine($"Installing to: [cyan]{Markup.Escape(programFilesPath)}[/]\n");

            // Create directory
            Directory.CreateDirectory(programFilesPath);

            // Copy binary
            var targetPath = Path.Combine(programFilesPath, "hpdos.exe");
            File.Copy(binaryPath, targetPath, overwrite: true);
            session.MarkupLine("[green]Copied binary[/]");

            // Add to PATH via registry
            AddToWindowsPath(session, programFilesPath);
            session.MarkupLine("[green]Added to Windows PATH[/]");

            session.MarkupLine("\n[green]Setup complete![/]");
            session.MarkupLine($"Please restart your terminal or run: [cyan]set PATH=%PATH%;{Markup.Escape(programFilesPath)}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            session.MarkupLine($"[red]Windows setup failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> SetupUnix(IConsoleSession session, string binaryPath)
    {
        try
        {
            const string installDir = "/usr/local/bin";
            const string binaryName = "hpdos";
            var targetPath = Path.Combine(installDir, binaryName);

            session.MarkupLine($"Installing to: [cyan]{Markup.Escape(targetPath)}[/]\n");

            // Check if we need sudo
            var needsSudo = !IsWritableDirectory(installDir);

            if (needsSudo)
            {
                session.MarkupLine("[yellow]This requires sudo permission[/]");
                session.MarkupLine("You will be prompted for your password.\n");

                // Use sudo to copy
                var result = await ExecuteCommandAsync("sudo", $"cp {binaryPath} {targetPath}");
                if (result != 0)
                {
                    session.MarkupLine("[red]Failed to copy binary[/]");
                    return 1;
                }

                // Make executable
                result = await ExecuteCommandAsync("sudo", $"chmod +x {targetPath}");
                if (result != 0)
                {
                    session.MarkupLine("[red]Failed to make executable[/]");
                    return 1;
                }
            }
            else
            {
                // Can write without sudo
                File.Copy(binaryPath, targetPath, overwrite: true);
                File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            session.MarkupLine("[green]Installed successfully[/]");

            // Verify
            var testResult = await ExecuteCommandAsync("which", binaryName);
            if (testResult == 0)
            {
                session.MarkupLine("\n[green]Setup complete![/]");
                session.MarkupLine("Run [cyan]hpdos --help[/] to get started");
                return 0;
            }

            session.MarkupLine("\n[yellow]Binary installed but not yet in PATH[/]");
            session.MarkupLine("Please open a new terminal or run: [cyan]source ~/.bashrc[/] (or ~/.zshrc)");
            return 0;
        }
        catch (Exception ex)
        {
            session.MarkupLine($"[red]Unix setup failed:[/] {Markup.Escape(ex.Message)}");
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

    private static void AddToWindowsPath(IConsoleSession session, string pathToAdd)
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
            session.MarkupLine($"[yellow]Warning:[/] Could not update registry: {Markup.Escape(ex.Message)}");
            session.MarkupLine("You may need to manually add to PATH");
        }
    }
}
