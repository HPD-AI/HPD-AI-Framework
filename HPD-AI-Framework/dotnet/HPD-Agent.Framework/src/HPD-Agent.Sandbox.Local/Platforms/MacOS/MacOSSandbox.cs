using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Security;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local.Platforms.MacOS;

/// <summary>
/// Enhanced macOS sandbox using sandbox-exec (Apple Seatbelt).
/// </summary>
/// <remarks>
/// <para><b>Improvements over base implementation:</b></para>
/// <list type="bullet">
/// <item>Glob pattern support via regex conversion</item>
/// <item>Move-blocking rules to prevent bypass via mv/rename</item>
/// <item>Automatic dangerous file protection</item>
/// <item>Better violation monitoring with command correlation</item>
/// </list>
/// </remarks>
internal sealed class MacOSSandbox : IPlatformSandbox
{
    private readonly SandboxConfig _config;
    private readonly IHttpProxyServer? _httpProxy;
    private readonly ISocks5ProxyServer? _socksProxy;
    private readonly ILogger? _logger;
    private readonly Channel<SandboxViolation> _violationChannel;
    private readonly string _sessionSuffix;
    private Process? _logStreamProcess;

    public MacOSSandbox(
        SandboxConfig config,
        IHttpProxyServer? httpProxy,
        ISocks5ProxyServer? socksProxy,
        ILogger? logger = null)
    {
        _config = config;
        _httpProxy = httpProxy;
        _socksProxy = socksProxy;
        _logger = logger;
        _violationChannel = Channel.CreateUnbounded<SandboxViolation>();
        _sessionSuffix = $"_{GenerateSessionId()}_SBX";
    }

    public ChannelReader<SandboxViolation>? Violations =>
        _config.EnableViolationMonitoring ? _violationChannel.Reader : null;

    public Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        // sandbox-exec is built into macOS, just verify it exists
        return Task.FromResult(File.Exists("/usr/bin/sandbox-exec"));
    }

    public async Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken)
    {
        var logTag = GenerateLogTag(command);

        // Build the profile
        var builder = new SeatbeltProfileBuilder(logTag);

        // 1. Add allowed write paths
        builder.AllowWrite(_config.AllowWrite);
        builder.AllowWrite(SandboxDefaults.DefaultWritePaths);

        // 2. Add denied read paths
        builder.DenyRead(_config.DenyRead);
        builder.DenyRead(SandboxDefaults.SensitiveDirectories);

        // 3. Add mandatory dangerous path protection
        var dangerousPaths = GetMandatoryDenyPatterns().ToList();
        builder.DenyWrite(dangerousPaths);

        // 4. Configure network
        var hasNetwork = _config.AllowedDomains?.Length > 0;

        // Use external proxy ports if configured, otherwise use internal
        var httpProxyPort = _config.ExternalHttpProxyPort ?? _httpProxy?.Port;
        var socksProxyPort = _config.ExternalSocksProxyPort ?? _socksProxy?.Port;

        if (_config.ExternalHttpProxyPort.HasValue)
            _logger?.LogDebug("Using external HTTP proxy on port {Port}", _config.ExternalHttpProxyPort.Value);
        if (_config.ExternalSocksProxyPort.HasValue)
            _logger?.LogDebug("Using external SOCKS5 proxy on port {Port}", _config.ExternalSocksProxyPort.Value);

        builder.WithNetwork(
            allowed: hasNetwork || _config.AllowedDomains == null,
            httpProxyPort: httpProxyPort,
            socksProxyPort: socksProxyPort);

        if (_config.AllowLocalBinding)
            builder.AllowLocalBinding();

        if (_config.AllowPty)
            builder.AllowPty();

        // 4b. Configure Unix socket access
        if (_config.AllowAllUnixSockets)
        {
            builder.AllowAllUnixSockets();
        }
        else if (_config.AllowUnixSockets?.Length > 0)
        {
            builder.AllowUnixSockets(_config.AllowUnixSockets);
        }

        // 5. Build profile
        var profile = builder.Build();

        // 6. Write profile to temp file
        var profilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(profilePath, profile, cancellationToken);

        // 7. Build environment prefix
        var envPrefix = BuildEnvironmentPrefix();

        // 8. Get shell
        var shell = GetShellPath();

        // 9. Build final command
        var wrappedCommand = $"sandbox-exec -f {QuoteArg(profilePath)} {shell} -c {QuoteArg(envPrefix + command)}";

        // 10. Start violation monitoring if enabled
        if (_config.EnableViolationMonitoring && _logStreamProcess == null)
        {
            StartViolationMonitoring();
        }

        _logger?.LogDebug(
            "Created macOS sandbox profile with {WriteCount} write paths, {DenyCount} deny paths",
            _config.AllowWrite.Length + SandboxDefaults.DefaultWritePaths.Count,
            dangerousPaths.Count);

        return wrappedCommand;
    }

    private string BuildEnvironmentPrefix()
    {
        var sb = new StringBuilder();

        // Sandbox runtime marker
        sb.Append("SANDBOX_RUNTIME=1 ");

        // Determine proxy ports - use external if configured, otherwise use internal
        var httpProxyPort = _config.ExternalHttpProxyPort ?? _httpProxy?.Port;
        var socksProxyPort = _config.ExternalSocksProxyPort ?? _socksProxy?.Port;

        // Proxy environment variables
        if (httpProxyPort.HasValue)
        {
            var httpProxy = $"http://127.0.0.1:{httpProxyPort.Value}";
            sb.Append($"HTTP_PROXY={httpProxy} ");
            sb.Append($"HTTPS_PROXY={httpProxy} ");
            sb.Append($"http_proxy={httpProxy} ");
            sb.Append($"https_proxy={httpProxy} ");
        }

        if (socksProxyPort.HasValue)
        {
            var socksProxy = $"socks5h://127.0.0.1:{socksProxyPort.Value}";
            sb.Append($"ALL_PROXY={socksProxy} ");
            sb.Append($"all_proxy={socksProxy} ");
        }

        // NO_PROXY for local addresses
        var noProxy = "localhost,127.0.0.1,::1,*.local,.local";
        sb.Append($"NO_PROXY={noProxy} ");
        sb.Append($"no_proxy={noProxy} ");

        return sb.ToString();
    }

    /// <summary>
    /// Gets mandatory deny patterns using globs (no filesystem scanning needed on macOS).
    /// </summary>
    private IEnumerable<string> GetMandatoryDenyPatterns()
    {
        var cwd = Environment.CurrentDirectory;
        var patterns = new List<string>();

        // Dangerous files in CWD and subtree
        foreach (var file in SandboxDefaults.DangerousFiles)
        {
            patterns.Add(Path.Combine(cwd, file));
            patterns.Add($"**/{file}");
        }

        // Dangerous directories
        foreach (var dir in SandboxDefaults.DangerousDirectories)
        {
            patterns.Add(Path.Combine(cwd, dir));
            patterns.Add($"**/{dir}/**");
        }

        // Git hooks always protected
        patterns.Add(Path.Combine(cwd, ".git/hooks"));
        patterns.Add("**/.git/hooks/**");

        // Git config conditionally protected
        if (!_config.AllowGitConfig)
        {
            patterns.Add(Path.Combine(cwd, ".git/config"));
            patterns.Add("**/.git/config");
        }

        return patterns.Distinct();
    }

    private void StartViolationMonitoring()
    {
        _logStreamProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "log",
                Arguments = $"stream --predicate '(eventMessage ENDSWITH \"{_sessionSuffix}\")'",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _logStreamProcess.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null && e.Data.Contains("deny"))
            {
                var violation = ParseViolation(e.Data);
                if (violation != null && !ShouldIgnoreViolation(violation))
                {
                    _violationChannel.Writer.TryWrite(violation);
                }
            }
        };

        _logStreamProcess.Start();
        _logStreamProcess.BeginOutputReadLine();

        _logger?.LogDebug("Started macOS sandbox log monitor with session suffix: {Suffix}", _sessionSuffix);
    }

    private SandboxViolation? ParseViolation(string logLine)
    {
        // Extract the sandbox violation details
        var sandboxIndex = logLine.IndexOf("Sandbox:", StringComparison.Ordinal);
        if (sandboxIndex == -1) return null;

        var details = logLine[(sandboxIndex + 8)..].Trim();

        // Determine violation type
        ViolationType type;
        if (details.Contains("file-read"))
            type = ViolationType.FilesystemRead;
        else if (details.Contains("file-write"))
            type = ViolationType.FilesystemWrite;
        else if (details.Contains("network"))
            type = ViolationType.NetworkAccess;
        else
            return null;

        // Extract path if present
        string? path = null;
        var pathMatch = System.Text.RegularExpressions.Regex.Match(details, @"(?:subpath|literal|regex)\s+""([^""]+)""");
        if (pathMatch.Success)
            path = pathMatch.Groups[1].Value;

        return new SandboxViolation
        {
            Type = type,
            Message = details,
            Path = path,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private bool ShouldIgnoreViolation(SandboxViolation violation)
    {
        // Filter out noisy system violations
        if (violation.Message.Contains("mDNSResponder") ||
            violation.Message.Contains("com.apple.diagnosticd") ||
            violation.Message.Contains("com.apple.analyticsd"))
        {
            return true;
        }

        // Check user-configured ignore patterns
        if (_config.IgnoreViolationPatterns != null)
        {
            foreach (var pattern in _config.IgnoreViolationPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(violation.Message, pattern) ||
                    (violation.Path != null && System.Text.RegularExpressions.Regex.IsMatch(violation.Path, pattern)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string GenerateLogTag(string command)
    {
        var truncated = command.Length > 100 ? command[..100] : command;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(truncated));
        return $"CMD64_{encoded}_END{_sessionSuffix}";
    }

    private static string GenerateSessionId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    }

    private static string GetShellPath()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;

        return "/bin/zsh"; // macOS default
    }

    private static string QuoteArg(string arg)
    {
        return $"'{arg.Replace("'", "'\\''")}'";
    }

    public async ValueTask DisposeAsync()
    {
        if (_logStreamProcess != null)
        {
            try
            {
                _logStreamProcess.Kill();
                await _logStreamProcess.WaitForExitAsync();
            }
            catch { }
            finally
            {
                _logStreamProcess.Dispose();
            }
        }

        _violationChannel.Writer.Complete();
    }
}
