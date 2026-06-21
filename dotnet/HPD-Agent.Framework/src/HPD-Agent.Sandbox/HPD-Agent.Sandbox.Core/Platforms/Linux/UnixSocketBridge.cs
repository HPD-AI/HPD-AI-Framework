using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.Sandbox.Platforms.Linux;

/// <summary>
/// Manages Unix socket bridges for Linux network sandboxing.
/// </summary>
/// <remarks>
/// <para><b>Why This is Needed:</b></para>
/// <para>
/// When using bwrap --unshare-net, the sandbox has NO network access at all.
/// localhost:port is unreachable because the network namespace is isolated.
/// </para>
///
/// <para><b>Solution:</b></para>
/// <para>
/// We use Unix domain sockets which work across namespaces when bind-mounted.
/// </para>
///
/// <para><b>Architecture:</b></para>
/// <code>
/// Host:     proxy server (localhost:random_port)
///               ↓
/// Host:     socat UNIX-LISTEN:socket TCP:localhost:port
///               ↓
/// [bind mount socket into namespace]
///               ↓
/// Sandbox:  socat TCP-LISTEN:3128 UNIX-CONNECT:socket
///               ↓
/// Sandbox:  user command with HTTP_PROXY=localhost:3128
/// </code>
/// </remarks>
public sealed class UnixSocketBridge : IAsyncDisposable
{
    private readonly ILogger? _logger;
    private readonly string _socatPath;
    private readonly List<Process> _bridgeProcesses = [];
    private readonly List<string> _socketPaths = [];
    private readonly ConcurrentQueue<string> _processOutput = new();
    private bool _disposed;

    public UnixSocketBridge(ILogger? logger = null, string socatPath = "socat")
    {
        _logger = logger;
        _socatPath = socatPath;
    }

    /// <summary>
    /// Path to the HTTP proxy Unix socket.
    /// </summary>
    public string? HttpSocketPath { get; private set; }

    /// <summary>
    /// Path to the SOCKS5 proxy Unix socket.
    /// </summary>
    public string? SocksSocketPath { get; private set; }

    /// <summary>
    /// Creates Unix socket bridges for the proxy servers.
    /// </summary>
    /// <param name="httpProxyPort">Port of the HTTP proxy server</param>
    /// <param name="socksProxyPort">Port of the SOCKS5 proxy server</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task InitializeAsync(
        int httpProxyPort,
        int socksProxyPort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var tmpDir = Path.GetTempPath();

        try
        {
            HttpSocketPath = Path.Combine(tmpDir, $"hpd-http-{sessionId}.sock");
            await StartBridgeAsync(HttpSocketPath, httpProxyPort, "HTTP", cancellationToken);

            SocksSocketPath = Path.Combine(tmpDir, $"hpd-socks-{sessionId}.sock");
            await StartBridgeAsync(SocksSocketPath, socksProxyPort, "SOCKS5", cancellationToken);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }

        _logger?.LogInformation(
            "Unix socket bridges created: HTTP={HttpSocket}, SOCKS={SocksSocket}",
            HttpSocketPath, SocksSocketPath);
    }

    private async Task StartBridgeAsync(
        string socketPath,
        int targetPort,
        string name,
        CancellationToken cancellationToken)
    {
        if (File.Exists(socketPath))
            File.Delete(socketPath);

        _socketPaths.Add(socketPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _socatPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        foreach (var argument in BuildHostSocatArguments(socketPath, targetPort))
            process.StartInfo.ArgumentList.Add(argument);

        process.Exited += (_, _) =>
        {
            _logger?.LogWarning(
                "{Name} bridge process exited unexpectedly with code {ExitCode}. Output tail: {Output}",
                name,
                SafeExitCode(process),
                GetProcessOutputTail());
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start {name} socket bridge process.");
        }
        catch
        {
            process.Dispose();
            throw;
        }

        DrainProcessOutput(process, name);
        _bridgeProcesses.Add(process);

        const int maxAttempts = 50;
        for (var i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"{name} socket bridge exited before creating {socketPath}. " +
                    $"Exit code: {process.ExitCode}. Output tail: {GetProcessOutputTail()}");
            }

            if (File.Exists(socketPath))
            {
                _logger?.LogDebug("{Name} bridge ready at {Socket}", name, socketPath);
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Failed to create {name} socket bridge at {socketPath} after {maxAttempts} attempts. " +
            $"Output tail: {GetProcessOutputTail()}");
    }

    /// <summary>
    /// Checks if socat is available on the system.
    /// </summary>
    public static async Task<bool> IsSocatAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "socat",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates the bwrap arguments to bind the sockets into the sandbox.
    /// </summary>
    public IEnumerable<string> GetBwrapBindArgs()
    {
        if (HttpSocketPath != null)
        {
            yield return "--bind";
            yield return HttpSocketPath;
            yield return HttpSocketPath;
        }

        if (SocksSocketPath != null)
        {
            yield return "--bind";
            yield return SocksSocketPath;
            yield return SocksSocketPath;
        }
    }

    /// <summary>
    /// Generates the shell commands to run inside the sandbox to set up the proxy listeners.
    /// </summary>
    /// <param name="httpPort">Port for HTTP proxy inside sandbox (default: 3128)</param>
    /// <param name="socksPort">Port for SOCKS proxy inside sandbox (default: 1080)</param>
    public string GetSandboxSetupScript(int httpPort = 3128, int socksPort = 1080)
    {
        var commands = new List<string>();

        if (HttpSocketPath != null)
            commands.Add(BuildSandboxSocatCommand(httpPort, HttpSocketPath));

        if (SocksSocketPath != null)
            commands.Add(BuildSandboxSocatCommand(socksPort, SocksSocketPath));

        commands.Add("trap 'kill $(jobs -p) 2>/dev/null' EXIT");
        return string.Join("\n", commands);
    }

    internal static string[] BuildHostSocatArguments(string socketPath, int targetPort) =>
    [
        $"UNIX-LISTEN:{socketPath},fork,reuseaddr",
        $"TCP:localhost:{targetPort},keepalive",
    ];

    internal static string BuildSandboxSocatCommand(int listenPort, string socketPath) =>
        $"socat TCP-LISTEN:{listenPort},fork,reuseaddr,keepalive UNIX-CONNECT:{QuoteShellArg(socketPath)} &";

    /// <summary>
    /// Generates environment variables for the sandboxed process.
    /// </summary>
    public Dictionary<string, string> GetProxyEnvironmentVariables(int httpPort = 3128, int socksPort = 1080)
    {
        var env = new Dictionary<string, string>
        {
            ["SANDBOX_RUNTIME"] = "1",
            ["TMPDIR"] = "/tmp/hpd"
        };

        var noProxy = "localhost,127.0.0.1,::1,*.local,.local,169.254.0.0/16,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";
        env["NO_PROXY"] = noProxy;
        env["no_proxy"] = noProxy;

        var httpProxy = $"http://localhost:{httpPort}";
        env["HTTP_PROXY"] = httpProxy;
        env["HTTPS_PROXY"] = httpProxy;
        env["http_proxy"] = httpProxy;
        env["https_proxy"] = httpProxy;

        var socksProxy = $"socks5h://localhost:{socksPort}";
        env["ALL_PROXY"] = socksProxy;
        env["all_proxy"] = socksProxy;

        return env;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var process in _bridgeProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(2));
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error killing bridge process");
            }
        }

        foreach (var socketPath in _socketPaths)
        {
            try
            {
                if (File.Exists(socketPath))
                    File.Delete(socketPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error deleting socket {Socket}", socketPath);
            }
        }

        _logger?.LogInformation("Unix socket bridges disposed");
    }

    private void DrainProcessOutput(Process process, string name)
    {
        process.OutputDataReceived += (_, e) => EnqueueProcessOutput(name, e.Data);
        process.ErrorDataReceived += (_, e) => EnqueueProcessOutput(name, e.Data);

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogDebug(ex, "Could not begin output draining for {Name} bridge", name);
        }
    }

    private void EnqueueProcessOutput(string name, string? line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        _processOutput.Enqueue($"{name}: {line}");
        while (_processOutput.Count > 20 && _processOutput.TryDequeue(out _))
        {
        }
    }

    private string GetProcessOutputTail() =>
        string.Join(" | ", _processOutput.ToArray());

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WaitForExitWithTimeoutAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static string QuoteShellArg(string value) =>
        $"'{value.Replace("'", "'\\''")}'";
}
