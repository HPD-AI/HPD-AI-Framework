using System.Threading.Channels;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Platforms.Linux.Seccomp;
using HPD.Sandbox.Local.Security;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Platforms.Linux;

/// <summary>
/// Linux sandbox using bubblewrap (bwrap) with network isolation via Unix socket bridges
/// and seccomp filtering to block Unix socket creation.
/// </summary>
/// <remarks>
/// <para><b>Architecture:</b></para>
/// <code>
/// ┌─────────────────────────────────────────────────────────────────┐
/// │ HOST                                                            │
/// │  ┌─────────────────┐    ┌──────────────────┐                   │
/// │  │ HTTP Proxy      │    │ SOCKS5 Proxy     │                   │
/// │  │ localhost:random│    │ localhost:random │                   │
/// │  └────────┬────────┘    └────────┬─────────┘                   │
/// │           │                      │                              │
/// │  ┌────────▼────────┐    ┌────────▼─────────┐                   │
/// │  │ socat bridge    │    │ socat bridge     │                   │
/// │  │ → Unix socket   │    │ → Unix socket    │                   │
/// │  └────────┬────────┘    └────────┬─────────┘                   │
/// └───────────┼──────────────────────┼──────────────────────────────┘
///             │ bind mount           │ bind mount
/// ┌───────────▼──────────────────────▼──────────────────────────────┐
/// │ SANDBOX (bwrap --unshare-net)                                   │
/// │                                                                  │
/// │  STAGE 1: Setup (no seccomp - can use Unix sockets)            │
/// │  ┌────────────────┐    ┌─────────────────┐                      │
/// │  │ socat listener │    │ socat listener  │                      │
/// │  │ TCP:3128       │    │ TCP:1080        │                      │
/// │  │ → Unix socket  │    │ → Unix socket   │                      │
/// │  └────────────────┘    └─────────────────┘                      │
/// │                                                                  │
/// │  STAGE 2: apply-seccomp (blocks socket(AF_UNIX, ...))          │
/// │  ┌─────────────────────────────────────────┐                    │
/// │  │ USER COMMAND                            │                    │
/// │  │ (isolated network namespace,            │                    │
/// │  │  read-only filesystem,                  │                    │
/// │  │  dangerous paths protected,             │                    │
/// │  │  Unix socket creation BLOCKED)          │                    │
/// │  └─────────────────────────────────────────┘                    │
/// └──────────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Security Layers:</b></para>
/// <list type="bullet">
/// <item>Network namespace isolation (--unshare-net)</item>
/// <item>PID namespace isolation (--unshare-pid)</item>
/// <item>Read-only root filesystem</item>
/// <item>Dangerous file write protection</item>
/// <item>Seccomp filter blocking Unix socket creation</item>
/// </list>
/// </remarks>
internal sealed class LinuxSandbox : IPlatformSandbox
{
    private readonly SandboxConfig _config;
    private readonly IHttpProxyServer? _httpProxy;
    private readonly ISocks5ProxyServer? _socksProxy;
    private readonly ILogger? _logger;
    private readonly DangerousPathScanner _pathScanner;
    private readonly SeccompChildProcess _seccompHelper;
    private UnixSocketBridge? _socketBridge;
    private bool _initialized;
    private string? _seccompHelperPath;

    public LinuxSandbox(
        SandboxConfig config,
        IHttpProxyServer? httpProxy,
        ISocks5ProxyServer? socksProxy,
        ILogger? logger = null)
    {
        _config = config;
        _httpProxy = httpProxy;
        _socksProxy = socksProxy;
        _logger = logger;
        _pathScanner = new DangerousPathScanner(_config.MandatoryDenySearchDepth);
        _seccompHelper = new SeccompChildProcess(logger);
    }

    public ChannelReader<SandboxViolation>? Violations => null; // Not supported on Linux

    public async Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        // Check for bwrap
        if (!await IsCommandAvailableAsync("bwrap", cancellationToken))
        {
            _logger?.LogError("bubblewrap (bwrap) is not installed");
            return false;
        }

        // Check for socat (needed for network bridging)
        if (_config.AllowedDomains?.Length > 0)
        {
            if (!await UnixSocketBridge.IsSocatAvailableAsync(cancellationToken))
            {
                _logger?.LogError("socat is not installed (required for network filtering)");
                return false;
            }
        }

        // Check for seccomp support (unless AllowAllUnixSockets is true)
        if (!_config.AllowAllUnixSockets)
        {
            if (!SeccompFilter.IsSupported)
            {
                _logger?.LogWarning(
                    "Seccomp is not supported on this system. " +
                    "Unix socket blocking will be disabled. " +
                    "Set AllowAllUnixSockets=true to suppress this warning.");
            }
            else
            {
                // Check for gcc (needed to build seccomp helper)
                if (!await IsCommandAvailableAsync("gcc", cancellationToken))
                {
                    _logger?.LogWarning(
                        "gcc is not installed. Unix socket blocking will be disabled. " +
                        "Install gcc or set AllowAllUnixSockets=true.");
                }
            }
        }

        return true;
    }

    public async Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken)
    {
        // Initialize socket bridge and seccomp helper if needed
        await EnsureInitializedAsync(cancellationToken);

        var builder = new BubblewrapBuilder();

        // 1. Read-only root filesystem
        builder.WithReadOnlyRoot();

        // 2. Add writable paths (user-specified + defaults)
        var writePaths = _config.AllowWrite
            .Concat(SandboxDefaults.DefaultWritePaths)
            .Distinct();
        builder.WithWritablePaths(writePaths);

        // 3. Add tmpfs for temp directory
        builder.WithTmpfs("/tmp");

        // 4. Deny read paths
        foreach (var path in _config.DenyRead)
        {
            builder.WithDeniedReadPath(path);
        }

        // 5. Add mandatory deny paths (dangerous files)
        var dangerousPaths = await _pathScanner.GetDangerousPathsAsync(
            Environment.CurrentDirectory,
            _config.AllowGitConfig,
            cancellationToken);

        builder.WithDeniedWritePaths(dangerousPaths);

        _logger?.LogDebug("Protected {Count} dangerous paths", dangerousPaths.Count);

        // 6. Essential system access
        builder.WithDevices();

        // 7. PID isolation (unless weaker sandbox mode)
        if (!_config.EnableWeakerNestedSandbox)
        {
            builder.WithPidIsolation(mountProc: true);
        }

        // 8. Pass through safe environment variables
        builder.WithSafeEnvironmentVariables();

        // 9. Network isolation and proxy setup
        var needsNetwork = _config.AllowedDomains?.Length > 0;
        var shell = GetShellPath();

        if (needsNetwork && _socketBridge != null)
        {
            // Isolate network namespace
            builder.WithNetworkIsolation();

            // Bind Unix sockets into the sandbox
            var socketPaths = new List<string>();
            if (_socketBridge.HttpSocketPath != null)
                socketPaths.Add(_socketBridge.HttpSocketPath);
            if (_socketBridge.SocksSocketPath != null)
                socketPaths.Add(_socketBridge.SocksSocketPath);

            builder.WithUnixSocketBinds(socketPaths);

            // Set proxy environment variables
            var proxyEnv = _socketBridge.GetProxyEnvironmentVariables();
            builder.WithEnvironmentVariables(proxyEnv);

            // Build command with setup script for internal socat listeners
            var setupScript = _socketBridge.GetSandboxSetupScript();

            // Use seccomp if available and not disabled
            if (_seccompHelperPath != null && !_config.AllowAllUnixSockets)
            {
                // Bind the seccomp helper into the sandbox
                builder.WithUnixSocketBinds([_seccompHelperPath]);

                _logger?.LogDebug("Using seccomp to block Unix socket creation");
                return builder.BuildWithSeccomp(setupScript, command, _seccompHelperPath, shell);
            }
            else
            {
                _logger?.LogDebug("Running without seccomp (Unix sockets allowed)");
                return builder.BuildWithSetup(setupScript, command, shell);
            }
        }
        else if (_config.AllowedDomains?.Length == 0)
        {
            // No network allowed at all
            builder.WithNetworkIsolation();
        }

        // For non-network cases, still apply seccomp if available
        if (_seccompHelperPath != null && !_config.AllowAllUnixSockets)
        {
            builder.WithUnixSocketBinds([_seccompHelperPath]);
            var seccompCommand = $"{QuoteArg(_seccompHelperPath)} {shell} -c {QuoteArg(command)}";
            return builder.Build(seccompCommand, shell);
        }

        return builder.Build(command, shell);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var needsNetwork = _config.AllowedDomains?.Length > 0;

        // Initialize network bridge if needed
        if (needsNetwork)
        {
            // Determine proxy ports - use external if configured, otherwise use internal
            int httpPort;
            int socksPort;

            if (_config.ExternalHttpProxyPort.HasValue)
            {
                httpPort = _config.ExternalHttpProxyPort.Value;
                _logger?.LogDebug("Using external HTTP proxy on port {Port}", httpPort);
            }
            else if (_httpProxy != null)
            {
                httpPort = _httpProxy.Port;
            }
            else
            {
                _logger?.LogWarning("Network requested but no HTTP proxy available");
                return;
            }

            if (_config.ExternalSocksProxyPort.HasValue)
            {
                socksPort = _config.ExternalSocksProxyPort.Value;
                _logger?.LogDebug("Using external SOCKS5 proxy on port {Port}", socksPort);
            }
            else
            {
                socksPort = _socksProxy?.Port ?? httpPort;
            }

            _socketBridge = new UnixSocketBridge(_logger);
            await _socketBridge.InitializeAsync(httpPort, socksPort, cancellationToken);

            _logger?.LogInformation(
                "Linux sandbox initialized with network bridges: HTTP={Http}, SOCKS={Socks}",
                _socketBridge.HttpSocketPath,
                _socketBridge.SocksSocketPath);
        }

        // Initialize seccomp helper if needed and available
        if (!_config.AllowAllUnixSockets && SeccompFilter.IsSupported)
        {
            try
            {
                _seccompHelperPath = await _seccompHelper.EnsureHelperAsync(cancellationToken);
                _logger?.LogInformation("Seccomp helper ready: {Path}", _seccompHelperPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Failed to initialize seccomp helper. Unix socket blocking will be disabled.");
                _seccompHelperPath = null;
            }
        }

        _initialized = true;
    }

    private static string QuoteArg(string arg)
    {
        return $"'{arg.Replace("'", "'\\''")}'";
    }

    private static string GetShellPath()
    {
        // Try to find a suitable shell
        var shells = new[] { "/bin/bash", "/bin/sh", "/usr/bin/bash", "/usr/bin/sh" };
        foreach (var shell in shells)
        {
            if (File.Exists(shell))
                return shell;
        }
        return "/bin/sh";
    }

    private static async Task<bool> IsCommandAvailableAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
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

    public async ValueTask DisposeAsync()
    {
        if (_socketBridge != null)
        {
            await _socketBridge.DisposeAsync();
            _socketBridge = null;
        }

        _seccompHelper.Dispose();
    }
}
