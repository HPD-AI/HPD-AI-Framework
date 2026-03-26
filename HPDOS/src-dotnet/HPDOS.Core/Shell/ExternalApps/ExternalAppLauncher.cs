using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace HPDOS.Core.Shell.ExternalApps;

/// <summary>
/// Launches and tracks external app processes (e.g. code-server).
/// Each app is identified by a string key. Re-launching a running app
/// returns the existing URL without spawning a second process.
/// </summary>
public sealed class ExternalAppLauncher : IDisposable
{
    record RunningApp(Process Process, string Url);

    readonly ConcurrentDictionary<string, RunningApp> _running = new();

    /// <summary>
    /// Launch an external app if not already running, then return its URL.
    /// </summary>
    public async Task<string> LaunchAsync(
        string appId,
        string executable,
        Func<int, string[]> buildArgs,
        Func<int, string> buildUrl,
        TimeSpan? timeout = null)
    {
        if (_running.TryGetValue(appId, out var existing) && !existing.Process.HasExited)
            return existing.Url;

        var port = FindFreePort();
        var args = buildArgs(port);
        var url  = buildUrl(port);

        executable = ResolveExecutable(appId, executable);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute  = false,
            CreateNoWindow   = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {executable}");

        var app = new RunningApp(process, url);
        _running[appId] = app;

        // Clean up entry when process exits.
        process.Exited += (_, _) => _running.TryRemove(appId, out _);
        process.EnableRaisingEvents = true;

        // Poll until the port accepts connections.
        await WaitForPortAsync(port, timeout ?? TimeSpan.FromSeconds(30));

        return url;
    }

    /// <summary>Stop a running app by ID.</summary>
    public void Stop(string appId)
    {
        if (_running.TryRemove(appId, out var app) && !app.Process.HasExited)
        {
            try { app.Process.Kill(entireProcessTree: true); } catch { }
        }
    }

    public void Dispose()
    {
        foreach (var app in _running.Values)
        {
            try { app.Process.Kill(entireProcessTree: true); } catch { }
        }
        _running.Clear();
    }

    static string ResolveExecutable(string appId, string executableName)
    {
        var managed = Path.Combine(HpdosDataPaths.Apps, appId, executableName);
        if (File.Exists(managed)) return managed;
        return executableName; // fall back to PATH
    }

    static int FindFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static async Task WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", port);
                return; // connected — port is open
            }
            catch
            {
                await Task.Delay(200);
            }
        }
        throw new TimeoutException($"App did not open port {port} within {timeout.TotalSeconds}s");
    }
}
