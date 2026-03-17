using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using HPDOS.Core.Shell;
using HPDOS.Shell.Server;
using Microsoft.AspNetCore.Builder;
using Spectre.Console;

namespace HPDOS.Shell.Shell;

public static class GUIMode
{
    // Kestrel instance held so StopServerAsync() can stop it on app exit.
    static WebApplication? _kestrel;

    /// <summary>
    /// DI service provider from the running Kestrel instance.
    /// Non-null after <see cref="StartServerAsync"/> returns successfully.
    /// </summary>
    public static IServiceProvider? Services => _kestrel?.Services;

    /// <summary>
    /// CLI browser/Linux path: starts Kestrel and opens the system browser.
    /// Blocks until Ctrl+C. Used from the console entry point.
    /// </summary>
    public static Task<int> RunBrowserAsync()
    {
        // Locate dev.sh via git root (works regardless of bin output depth).
        var gitRoot = GetGitRoot();
        var devSh = gitRoot != null
            ? Path.Combine(gitRoot, "scripts", "dev.sh")
            : null;

        if (devSh == null || !File.Exists(devSh))
        {
            AnsiConsole.MarkupLine("[red]dev.sh not found.[/] Run [cyan]./scripts/dev.sh[/] from the repo root.");
            return Task.FromResult(1);
        }

        var psi = new ProcessStartInfo("bash", devSh)
        {
            UseShellExecute = false,
        };
        var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return Task.FromResult(proc.ExitCode);
    }

    static string? GetGitRoot()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("git", "rev-parse --show-toplevel")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            })!;
            var root = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 ? root : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Public server path: `hpdos serve [--port N]`.
    /// Binds to 0.0.0.0 so the server is reachable from other devices.
    /// Forces auth on regardless of appsettings. Blocks until Ctrl+C.
    /// </summary>
    public static async Task<int> RunServeAsync(string[] args)
    {
        // Parse optional --port N
        int port = 5000;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
                port = p;
        }

        ShellConfig.IsServeMode = true; // forces auth on in KestrelHostBuilder

        var cts     = new CancellationTokenSource();
        var kestrel = KestrelHostBuilder.Build(port, Array.Empty<string>());

        // Override the URL set by Build() to bind on all interfaces, not just localhost.
        kestrel.Urls.Clear();
        kestrel.Urls.Add($"http://0.0.0.0:{port}");

        try
        {
            await kestrel.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Server failed to start:[/] {ex.Message}");
            return 1;
        }

        _kestrel = kestrel;
        ShellConfig.Port             = port;
        ShellConfig.ShutdownToken    = cts.Token;
        ShellConfig.RequestShutdown  = () => cts.Cancel();
        kestrel.Lifetime.ApplicationStopped.Register(() => cts.Cancel());

        AnsiConsole.MarkupLine($"[green]HPDOS server running[/] → [link]http://0.0.0.0:{port}[/]");
        AnsiConsole.MarkupLine("[dim]Auth required. Press Ctrl+C to stop.[/]");

        static void TryShutdown() { try { ShellConfig.RequestShutdown?.Invoke(); } catch (ObjectDisposedException) { } }
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; TryShutdown(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryShutdown();

        try { await Task.Delay(Timeout.Infinite, ShellConfig.ShutdownToken); }
        catch (OperationCanceledException) { }

        await StopServerAsync();
        return 0;
    }

    /// <summary>
    /// Dev path: starts Kestrel on port 5000 and blocks until Ctrl+C. No browser opened.
    /// Used by dev.sh so the script controls when to open the browser.
    /// </summary>
    public static async Task<int> RunBackendAsync()
    {
        await StartServerAsync();
        if (ShellConfig.Port == 0) return 1;

        static void TryShutdown() { try { ShellConfig.RequestShutdown?.Invoke(); } catch (ObjectDisposedException) { } }
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; TryShutdown(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryShutdown();

        AnsiConsole.MarkupLine($"[green]Backend ready[/] → http://localhost:{ShellConfig.Port}");
        try { await Task.Delay(Timeout.Infinite, ShellConfig.ShutdownToken); }
        catch (OperationCanceledException) { }

        await StopServerAsync();
        return 0;
    }

    /// <summary>
    /// MAUI path: starts Kestrel in the background and returns immediately.
    /// Called from MauiProgram's lifecycle hook before the WebView loads.
    /// </summary>
    public static async Task StartServerAsync()
    {
        var cts = new CancellationTokenSource();

        var devMode = Environment.GetEnvironmentVariable("HPDOS_DEV") == "1";
        var port = devMode ? FindFreePortPreferring(5173) : FindFreePort();
        var kestrel = KestrelHostBuilder.Build(port, Array.Empty<string>());

        try
        {
            await kestrel.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Kestrel failed to start:[/] {ex.Message}");
            cts.Dispose();
            return;
        }

        kestrel.Lifetime.ApplicationStopped.Register(() => cts.Cancel());

        _kestrel = kestrel;
        ShellConfig.Port = port;
        ShellConfig.ShutdownToken = cts.Token;
        ShellConfig.RequestShutdown = () => cts.Cancel();

        // Write port file so `hpdos chat` can attach to this instance.
        try
        {
            Directory.CreateDirectory(HpdosDataPaths.Root);
            await File.WriteAllTextAsync(HpdosDataPaths.ActivePortFile, port.ToString());
        }
        catch { /* non-fatal */ }

        // Clean up port file on process exit (Ctrl+C handled by ChatCommand; this covers SIGTERM/kill).
        static void TryCleanup() { try { File.Delete(HpdosDataPaths.ActivePortFile); } catch { } }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryCleanup();
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true; // suppress default termination so we can clean up first
            TryCleanup();
            Environment.Exit(0);
        });
    }

    /// <summary>
    /// Stops Kestrel gracefully. Called when the MAUI window is closing.
    /// </summary>
    public static async Task StopServerAsync()
    {
        try { File.Delete(HpdosDataPaths.ActivePortFile); } catch { }
        if (_kestrel != null)
            await _kestrel.StopAsync();
    }

    static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static int FindFreePortPreferring(int preferred)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, preferred);
            listener.Start();
            listener.Stop();
            return preferred;
        }
        catch
        {
            return FindFreePort();
        }
    }

    static void OpenSystemBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not open browser:[/] {ex.Message}");
            AnsiConsole.MarkupLine($"Open manually: [link]{url}[/]");
        }
    }
}
