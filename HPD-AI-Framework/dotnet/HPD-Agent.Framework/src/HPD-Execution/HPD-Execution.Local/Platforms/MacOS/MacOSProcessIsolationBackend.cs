using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using HPD.Execution.Contracts;
using HPD.Execution.Local.Network;
using HPD.Execution.Local.ProcessIsolation;
using HPD.Execution.Local.Security;
using HPD.Execution.Local.State;
using Microsoft.Extensions.Logging;

namespace HPD.Execution.Local.Platforms.MacOS;

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
internal sealed class MacOSProcessIsolationBackend : ILocalProcessIsolationBackend
{
    private readonly LocalProcessIsolationPlan _plan;
    private readonly IHttpProxyServer? _httpProxy;
    private readonly ISocks5ProxyServer? _socksProxy;
    private readonly ILogger? _logger;
    private readonly Channel<ProcessIsolationViolation> _violationChannel;
    private readonly ProcessIsolationViolationStore _violationStore;
    private readonly string _sessionSuffix;
    private Process? _logStreamProcess;

    public MacOSProcessIsolationBackend(
        LocalProcessIsolationPlan plan,
        IHttpProxyServer? httpProxy,
        ISocks5ProxyServer? socksProxy,
        ILogger? logger = null)
    {
        _plan = plan;
        _httpProxy = httpProxy;
        _socksProxy = socksProxy;
        _logger = logger;
        _violationChannel = Channel.CreateUnbounded<ProcessIsolationViolation>();
        _violationStore = new ProcessIsolationViolationStore();
        _sessionSuffix = $"_{GenerateSessionId()}_SBX";
    }

    public ChannelReader<ProcessIsolationViolation>? Violations =>
        _plan.Violations.Action is not ProcessViolationAction.ProviderDefault ? _violationChannel.Reader : null;

    internal ProcessIsolationViolationStore ViolationStore => _violationStore;

    public Task<ProcessIsolationDependencyCheck> GetDependencyCheckAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!File.Exists("/usr/bin/sandbox-exec"))
            errors.Add("/usr/bin/sandbox-exec is not available");

        if (_plan.Violations.Action is not ProcessViolationAction.ProviderDefault && !File.Exists("/usr/bin/log"))
            warnings.Add("/usr/bin/log is not available; macOS violation monitoring will be disabled");

        foreach (var error in errors)
            _logger?.LogError("{DependencyError}", error);
        foreach (var warning in warnings)
            _logger?.LogWarning("{DependencyWarning}", warning);

        return Task.FromResult(new ProcessIsolationDependencyCheck
        {
            Errors = errors,
            Warnings = warnings,
        });
    }

    public async Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken) =>
        (await GetDependencyCheckAsync(cancellationToken)).IsAvailable;

    public Task<PreparedLocalProcessCommand> WrapCommandAsync(CommandInvocation command, CancellationToken cancellationToken)
    {
        return WrapShellCommandAsync(PosixShellQuoter.RenderCommand(command), cancellationToken);
    }

    public Task<PreparedLocalProcessCommand> WrapCommandAsync(
        CommandInvocation command,
        LocalProcessIsolationPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return WrapShellCommandAsync(PosixShellQuoter.RenderCommand(command), plan, cancellationToken);
    }

    public async Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken)
    {
        var wrapped = await WrapShellCommandAsync(command, cancellationToken);
        var envPrefix = BuildEnvironmentPrefix(wrapped.Environment);
        return $"{wrapped.FileName} {string.Join(" ", wrapped.ArgumentList.Take(2).Select(QuoteArg))} " +
            $"{QuoteArg(wrapped.ArgumentList[2])} {QuoteArg(wrapped.ArgumentList[3])} " +
            $"{QuoteArg(envPrefix + command)}";
    }

    private Task<PreparedLocalProcessCommand> WrapShellCommandAsync(string command, CancellationToken cancellationToken)
        => WrapShellCommandAsync(command, plan: null, cancellationToken);

    private Task<PreparedLocalProcessCommand> WrapShellCommandAsync(
        string command,
        LocalProcessIsolationPlan? plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logTag = GenerateLogTag(command);

        // Build the profile
        var builder = new SeatbeltProfileBuilder(logTag);

        var dangerousPaths = plan?.Filesystem.DangerousPaths.ProtectSensitiveDefaults is false
            ? []
            : GetMandatoryDenyPatterns().ToList();

        var effectivePlan = plan ?? _plan;

        builder.AllowWrite(LocalProcessIsolationDefaults.DefaultWritePaths);
        builder.WithFilesystemPlan(
            effectivePlan.Filesystem,
            dangerousPaths.Concat(effectivePlan.Filesystem.DangerousPaths.AdditionalDeniedWrites.Select(path => path.Value)));

        if (effectivePlan.Filesystem.DangerousPaths.ProtectSensitiveDefaults)
            builder.DenyRead(LocalProcessIsolationDefaults.SensitiveDirectories);

        builder.DenyRead(effectivePlan.Filesystem.DangerousPaths.AdditionalDeniedReads.Select(path => path.Value));

        // 4. Configure network
        var hasNetwork = effectivePlan.Network.Mode == NetworkEgressMode.Filtered;

        // Use external proxy ports if configured, otherwise use internal
        var httpProxyPort = _httpProxy?.Port;
        var socksProxyPort = _socksProxy?.Port;

        builder.WithNetwork(
            allowed: hasNetwork ||
                effectivePlan.Network.Mode == NetworkEgressMode.Unrestricted,
            httpProxyPort: httpProxyPort,
            socksProxyPort: socksProxyPort);

        if (effectivePlan.Interactive.AllowLocalBinding)
            builder.AllowLocalBinding();

        if (effectivePlan.Interactive.AllowPty)
            builder.AllowPty();

        builder.AllowMachLookup(effectivePlan.Interactive.AllowedMachLookups);

        // 4b. Configure Unix socket access
        if (effectivePlan.UnixSockets.AllowAll)
        {
            builder.AllowAllUnixSockets();
        }
        else if (effectivePlan.UnixSockets.AllowedSockets.Count > 0)
        {
            builder.AllowUnixSockets(effectivePlan.UnixSockets.AllowedUnixSocketPaths());
        }

        // 5. Build profile
        var profile = builder.Build();

        // 6. Build environment variables
        var environment = BuildEnvironment(plan);

        // 7. Get shell
        var shell = GetShellPath();

        // 8. Build final command. Inline -p avoids temp profile lifecycle leaks.
        var wrappedCommand = new PreparedLocalProcessCommand(
            "sandbox-exec",
            ["-p", profile, shell, "-c", command],
            environment);

        // 9. Start violation monitoring if enabled
        if (effectivePlan.Violations.Action is not ProcessViolationAction.ProviderDefault && _logStreamProcess == null)
        {
            StartViolationMonitoring();
        }

        _logger?.LogDebug(
            "Created macOS sandbox profile with {WriteCount} write paths, {DenyCount} deny paths",
            effectivePlan.Filesystem.AllowWritePaths().Count + LocalProcessIsolationDefaults.DefaultWritePaths.Count,
            dangerousPaths.Count);

        return Task.FromResult(wrappedCommand);
    }

    private IReadOnlyDictionary<string, string> BuildEnvironment(LocalProcessIsolationPlan? plan = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["SANDBOX_RUNTIME"] = "1"
        };

        // Determine proxy ports - use external if configured, otherwise use internal
        var httpProxyPort = _httpProxy?.Port;
        var socksProxyPort = _socksProxy?.Port;

        // Proxy environment variables
        if (httpProxyPort.HasValue)
        {
            var httpProxy = $"http://127.0.0.1:{httpProxyPort.Value}";
            environment["HTTP_PROXY"] = httpProxy;
            environment["HTTPS_PROXY"] = httpProxy;
            environment["http_proxy"] = httpProxy;
            environment["https_proxy"] = httpProxy;
        }

        if (socksProxyPort.HasValue)
        {
            var socksProxy = $"socks5h://127.0.0.1:{socksProxyPort.Value}";
            environment["ALL_PROXY"] = socksProxy;
            environment["all_proxy"] = socksProxy;
        }

        // NO_PROXY for local addresses
        var noProxy = "localhost,127.0.0.1,::1,*.local,.local";
        environment["NO_PROXY"] = noProxy;
        environment["no_proxy"] = noProxy;

        if (plan is not null)
        {
            foreach (var (key, value) in plan.Environment.InjectedVariables)
                environment[key] = value;
        }

        return environment;
    }

    private static string BuildEnvironmentPrefix(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var (key, value) in environment)
        {
            sb.Append(key);
            sb.Append('=');
            sb.Append(value);
            sb.Append(' ');
        }

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
        foreach (var file in LocalProcessIsolationDefaults.DangerousFiles)
        {
            patterns.Add(Path.Combine(cwd, file));
            patterns.Add($"**/{file}");
        }

        // Dangerous directories
        foreach (var dir in LocalProcessIsolationDefaults.DangerousDirectories)
        {
            patterns.Add(Path.Combine(cwd, dir));
            patterns.Add($"**/{dir}/**");
        }

        // Git hooks always protected
        patterns.Add(Path.Combine(cwd, ".git/hooks"));
        patterns.Add("**/.git/hooks/**");

        // Git config conditionally protected
        patterns.Add(Path.Combine(cwd, ".git/config"));
        patterns.Add("**/.git/config");

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
                    _violationStore.Add(violation);
                    _violationChannel.Writer.TryWrite(violation);
                }
            }
        };

        _logStreamProcess.Start();
        _logStreamProcess.BeginOutputReadLine();

        _logger?.LogDebug("Started macOS sandbox log monitor with session suffix: {Suffix}", _sessionSuffix);
    }

    private ProcessIsolationViolation? ParseViolation(string logLine)
    {
        // Extract the sandbox violation details
        var sandboxIndex = logLine.IndexOf("Sandbox:", StringComparison.Ordinal);
        if (sandboxIndex == -1) return null;

        var details = logLine[(sandboxIndex + 8)..].Trim();

        // Determine violation type
        ProcessIsolationViolationType type;
        if (details.Contains("file-read"))
            type = ProcessIsolationViolationType.FilesystemRead;
        else if (details.Contains("file-write"))
            type = ProcessIsolationViolationType.FilesystemWrite;
        else if (details.Contains("network"))
            type = ProcessIsolationViolationType.NetworkAccess;
        else
            return null;

        // Extract path if present
        string? path = null;
        var pathMatch = System.Text.RegularExpressions.Regex.Match(details, @"(?:subpath|literal|regex)\s+""([^""]+)""");
        if (pathMatch.Success)
            path = pathMatch.Groups[1].Value;

        return new ProcessIsolationViolation
        {
            Type = type,
            Message = details,
            Path = path,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private bool ShouldIgnoreViolation(ProcessIsolationViolation violation)
    {
        // Filter out noisy system violations
        if (violation.Message.Contains("mDNSResponder") ||
            violation.Message.Contains("com.apple.diagnosticd") ||
            violation.Message.Contains("com.apple.analyticsd"))
        {
            return true;
        }

        // Check user-configured ignore patterns
        if (_plan.Violations.IgnorePatterns.Count > 0)
        {
            foreach (var pattern in _plan.Violations.IgnorePatterns)
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
