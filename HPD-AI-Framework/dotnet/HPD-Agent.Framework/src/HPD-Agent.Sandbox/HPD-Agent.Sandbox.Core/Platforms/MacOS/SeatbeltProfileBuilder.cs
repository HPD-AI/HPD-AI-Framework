using System.Text;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Agent.Sandbox.Security;

namespace HPD.Agent.Sandbox.Platforms.MacOS;

/// <summary>
/// Fluent builder for macOS sandbox (Seatbelt) profiles.
/// </summary>
/// <remarks>
/// <para><b>Profile Structure:</b></para>
/// <code>
/// (version 1)
/// (deny default (with message "LogTag"))
///
/// ; Allow rules first
/// (allow process-exec)
/// (allow file-read*)
///
/// ; Then deny rules (more specific)
/// (deny file-write* (subpath "/protected"))
/// </code>
///
/// <para><b>Security Features:</b></para>
/// <list type="bullet">
/// <item>Move-blocking rules prevent bypass via mv/rename</item>
/// <item>Glob patterns converted to regex for flexible matching</item>
/// <item>Ancestor directory protection for complete coverage</item>
/// </list>
/// </remarks>
public sealed class SeatbeltProfileBuilder
{
    private static readonly string[] AllowedSysctlNames =
    [
        "hw.activecpu",
        "hw.busfrequency_compat",
        "hw.byteorder",
        "hw.cacheconfig",
        "hw.cachelinesize_compat",
        "hw.cpufamily",
        "hw.cpufrequency",
        "hw.cpufrequency_compat",
        "hw.cputype",
        "hw.l1dcachesize_compat",
        "hw.l1icachesize_compat",
        "hw.l2cachesize_compat",
        "hw.l3cachesize_compat",
        "hw.logicalcpu",
        "hw.logicalcpu_max",
        "hw.machine",
        "hw.memsize",
        "hw.ncpu",
        "hw.nperflevels",
        "hw.packages",
        "hw.pagesize_compat",
        "hw.pagesize",
        "hw.physicalcpu",
        "hw.physicalcpu_max",
        "hw.tbfrequency_compat",
        "hw.vectorunit",
        "kern.argmax",
        "kern.bootargs",
        "kern.hostname",
        "kern.maxfiles",
        "kern.maxfilesperproc",
        "kern.maxproc",
        "kern.ngroups",
        "kern.osproductversion",
        "kern.osrelease",
        "kern.ostype",
        "kern.osvariant_status",
        "kern.osversion",
        "kern.secure_kernel",
        "kern.tcsm_available",
        "kern.tcsm_enable",
        "kern.usrstack64",
        "kern.version",
        "kern.willshutdown",
        "machdep.cpu.brand_string",
        "machdep.ptrauth_enabled",
        "security.mac.lockdown_mode_state",
        "sysctl.proc_cputype",
        "vm.loadavg",
    ];

    private static readonly string[] AllowedSysctlPrefixes =
    [
        "hw.optional.arm",
        "hw.optional.arm.",
        "hw.optional.armv8_",
        "hw.perflevel",
        "kern.proc.all",
        "kern.proc.pgrp.",
        "kern.proc.pid.",
        "machdep.cpu.",
        "net.routetable.",
    ];

    private readonly StringBuilder _profile = new();
    private readonly string _logTag;
    private readonly HashSet<string> _allowedWritePaths = [];
    private readonly HashSet<string> _deniedWritePaths = [];
    private readonly HashSet<string> _deniedReadPaths = [];
    private readonly HashSet<string> _allowedReadPaths = [];
    private readonly HashSet<string> _allowedUnixSockets = [];
    private readonly HashSet<string> _allowedMachLookups = [];
    private bool _networkAllowed = true;
    private int? _httpProxyPort;
    private int? _socksProxyPort;
    private bool _allowPty;
    private bool _allowLocalBinding;
    private bool _allowAllUnixSockets;
    private bool _allowTrustdLookup;

    /// <summary>
    /// Creates a new profile builder with the specified log tag.
    /// </summary>
    /// <param name="logTag">Unique identifier for this sandbox session (for violation tracking)</param>
    public SeatbeltProfileBuilder(string logTag)
    {
        _logTag = logTag;
    }

    /// <summary>
    /// Allows write access to a path.
    /// </summary>
    public SeatbeltProfileBuilder AllowWrite(string path)
    {
        _allowedWritePaths.Add(path);
        return this;
    }

    /// <summary>
    /// Allows write access to multiple paths.
    /// </summary>
    public SeatbeltProfileBuilder AllowWrite(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _allowedWritePaths.Add(path);
        return this;
    }

    /// <summary>
    /// Denies write access to a path.
    /// </summary>
    public SeatbeltProfileBuilder DenyWrite(string path)
    {
        _deniedWritePaths.Add(path);
        return this;
    }

    /// <summary>
    /// Denies write access to multiple paths.
    /// </summary>
    public SeatbeltProfileBuilder DenyWrite(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _deniedWritePaths.Add(path);
        return this;
    }

    /// <summary>
    /// Denies read access to a path.
    /// </summary>
    public SeatbeltProfileBuilder DenyRead(string path)
    {
        _deniedReadPaths.Add(path);
        return this;
    }

    /// <summary>
    /// Denies read access to multiple paths.
    /// </summary>
    public SeatbeltProfileBuilder DenyRead(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _deniedReadPaths.Add(path);
        return this;
    }

    /// <summary>
    /// Allows read access to a path inside a denied read region.
    /// </summary>
    public SeatbeltProfileBuilder AllowRead(string path)
    {
        _allowedReadPaths.Add(path);
        return this;
    }

    /// <summary>
    /// Allows read access to multiple paths inside denied read regions.
    /// </summary>
    public SeatbeltProfileBuilder AllowRead(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _allowedReadPaths.Add(path);
        return this;
    }

    internal SeatbeltProfileBuilder WithFilesystemPlan(
        SandboxFilesystemIsolationPlan filesystem,
        IEnumerable<string> mandatoryDenyWritePaths)
    {
        AllowWrite(filesystem.AllowWritePaths());
        DenyRead(filesystem.DenyReadPaths());
        AllowRead(filesystem.AllowReadPaths());
        DenyWrite(mandatoryDenyWritePaths.Concat(filesystem.DenyWritePaths()));
        return this;
    }

    /// <summary>
    /// Configures network access.
    /// </summary>
    /// <param name="allowed">Whether network is allowed</param>
    /// <param name="httpProxyPort">HTTP proxy port (if filtered)</param>
    /// <param name="socksProxyPort">SOCKS proxy port (if filtered)</param>
    public SeatbeltProfileBuilder WithNetwork(bool allowed, int? httpProxyPort = null, int? socksProxyPort = null)
    {
        _networkAllowed = allowed;
        _httpProxyPort = httpProxyPort;
        _socksProxyPort = socksProxyPort;
        return this;
    }

    /// <summary>
    /// Allows pseudo-terminal (pty) operations.
    /// </summary>
    public SeatbeltProfileBuilder AllowPty()
    {
        _allowPty = true;
        return this;
    }

    /// <summary>
    /// Allows binding to local ports.
    /// </summary>
    public SeatbeltProfileBuilder AllowLocalBinding()
    {
        _allowLocalBinding = true;
        return this;
    }

    /// <summary>
    /// Allows macOS trustd lookup for compatibility with TLS stacks that require it.
    /// </summary>
    public SeatbeltProfileBuilder AllowTrustdLookup()
    {
        _allowTrustdLookup = true;
        return this;
    }

    /// <summary>
    /// Allows all Unix socket operations.
    /// </summary>
    /// <remarks>
    /// <para>Warning: This allows access to all Unix sockets on the system.</para>
    /// <para>Prefer AllowUnixSockets(paths) for specific socket access.</para>
    /// </remarks>
    public SeatbeltProfileBuilder AllowAllUnixSockets()
    {
        _allowAllUnixSockets = true;
        return this;
    }

    /// <summary>
    /// Allows access to specific Unix socket paths.
    /// </summary>
    /// <param name="socketPaths">Paths to Unix sockets (e.g., /var/run/docker.sock)</param>
    public SeatbeltProfileBuilder AllowUnixSockets(IEnumerable<string> socketPaths)
    {
        foreach (var path in socketPaths)
            _allowedUnixSockets.Add(path);
        return this;
    }

    /// <summary>
    /// Allows access to a specific Unix socket path.
    /// </summary>
    /// <param name="socketPath">Path to Unix socket (e.g., /var/run/docker.sock)</param>
    public SeatbeltProfileBuilder AllowUnixSocket(string socketPath)
    {
        _allowedUnixSockets.Add(socketPath);
        return this;
    }

    /// <summary>
    /// Allows additional Mach lookup services.
    /// </summary>
    public SeatbeltProfileBuilder AllowMachLookup(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
            _allowedMachLookups.Add(pattern);
        return this;
    }

    /// <summary>
    /// Allows an additional Mach lookup service.
    /// </summary>
    public SeatbeltProfileBuilder AllowMachLookup(string pattern)
    {
        _allowedMachLookups.Add(pattern);
        return this;
    }

    /// <summary>
    /// Builds the complete sandbox profile.
    /// </summary>
    public string Build()
    {
        _profile.Clear();

        // Header
        _profile.AppendLine("(version 1)");
        _profile.AppendLine($"(deny default (with message \"{_logTag}\"))");
        _profile.AppendLine();
        _profile.AppendLine($"; LogTag: {_logTag}");
        _profile.AppendLine();

        // Essential permissions
        AddEssentialPermissions();

        // Network rules
        AddNetworkRules();

        // File read rules
        AddReadRules();

        // File write rules
        AddWriteRules();

        // PTY support
        if (_allowPty)
            AddPtySupport();

        // Unix socket rules
        AddUnixSocketRules();

        // Additional Mach lookup rules
        AddConfiguredMachLookupRules();

        return _profile.ToString();
    }

    private void AddEssentialPermissions()
    {
        _profile.AppendLine("; Essential permissions");
        _profile.AppendLine("; Process permissions");
        _profile.AppendLine("(allow process-exec)");
        _profile.AppendLine("(allow process-fork)");
        _profile.AppendLine("(allow process-info* (target same-sandbox))");
        _profile.AppendLine("(allow signal (target same-sandbox))");
        _profile.AppendLine("(allow mach-priv-task-port (target same-sandbox))");
        _profile.AppendLine();
        _profile.AppendLine("; User preferences");
        _profile.AppendLine("(allow user-preference-read)");
        _profile.AppendLine();
        _profile.AppendLine("; Mach IPC - specific services");
        _profile.AppendLine("(allow mach-lookup");
        _profile.AppendLine("  (global-name \"com.apple.FontObjectsServer\")");
        _profile.AppendLine("  (global-name \"com.apple.fonts\")");
        _profile.AppendLine("  (global-name \"com.apple.logd\")");
        _profile.AppendLine("  (global-name \"com.apple.system.logger\")");
        _profile.AppendLine("  (global-name \"com.apple.SecurityServer\")");
        if (_allowTrustdLookup)
            _profile.AppendLine("  (global-name \"com.apple.trustd.agent\")");
        _profile.AppendLine(")");
        _profile.AppendLine();
        _profile.AppendLine("; POSIX IPC");
        _profile.AppendLine("(allow ipc-posix-shm)");
        _profile.AppendLine("(allow ipc-posix-sem)");
        _profile.AppendLine();
        _profile.AppendLine("; IOKit");
        _profile.AppendLine("(allow iokit-get-properties)");
        _profile.AppendLine();
        AddSysctlRules();
        _profile.AppendLine("; Device I/O");
        _profile.AppendLine("(allow file-ioctl (literal \"/dev/null\"))");
        _profile.AppendLine("(allow file-ioctl (literal \"/dev/tty\"))");
        _profile.AppendLine();
    }

    private void AddSysctlRules()
    {
        _profile.AppendLine("; Sysctl - specific sysctls only");
        _profile.AppendLine("(allow sysctl-read");
        foreach (var name in AllowedSysctlNames)
            _profile.AppendLine($"  (sysctl-name \"{name}\")");
        foreach (var prefix in AllowedSysctlPrefixes)
            _profile.AppendLine($"  (sysctl-name-prefix \"{prefix}\")");
        _profile.AppendLine(")");
        _profile.AppendLine();
        _profile.AppendLine("; V8 thread calculations");
        _profile.AppendLine("(allow sysctl-write");
        _profile.AppendLine("  (sysctl-name \"kern.tcsm_enable\")");
        _profile.AppendLine(")");
        _profile.AppendLine();
    }

    private void AddNetworkRules()
    {
        _profile.AppendLine("; Network");

        var hasProxyPorts = _httpProxyPort.HasValue || _socksProxyPort.HasValue;

        if (!_networkAllowed && !hasProxyPorts)
        {
            _profile.AppendLine("(deny network*)");
        }
        else if (_networkAllowed && !hasProxyPorts)
        {
            _profile.AppendLine("(allow network*)");

            if (_allowLocalBinding)
                AddLocalBindingRules();
        }
        else
        {
            if (_allowLocalBinding)
                AddLocalBindingRules();

            if (_httpProxyPort.HasValue)
            {
                _profile.AppendLine($"(allow network-bind (local ip \"localhost:{_httpProxyPort}\"))");
                _profile.AppendLine($"(allow network-inbound (local ip \"localhost:{_httpProxyPort}\"))");
                _profile.AppendLine($"(allow network-outbound (remote ip \"localhost:{_httpProxyPort}\"))");
            }
            if (_socksProxyPort.HasValue)
            {
                _profile.AppendLine($"(allow network-bind (local ip \"localhost:{_socksProxyPort}\"))");
                _profile.AppendLine($"(allow network-inbound (local ip \"localhost:{_socksProxyPort}\"))");
                _profile.AppendLine($"(allow network-outbound (remote ip \"localhost:{_socksProxyPort}\"))");
            }
        }

        _profile.AppendLine();
    }

    private void AddLocalBindingRules()
    {
        // Use *:* for local endpoints because dual-stack runtimes may represent
        // 127.0.0.1 binds as IPv4-mapped IPv6 (::ffff:127.0.0.1).
        _profile.AppendLine("(allow network-bind (local ip \"*:*\"))");
        _profile.AppendLine("(allow network-inbound (local ip \"*:*\"))");
        _profile.AppendLine("(allow network-outbound (local ip \"*:*\"))");
    }

    private void AddReadRules()
    {
        _profile.AppendLine("; File read");
        _profile.AppendLine("(allow file-read*)");

        foreach (var path in _deniedReadPaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddDenyRule("file-read*", normalized);
        }

        foreach (var path in _allowedReadPaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddAllowRule("file-read*", normalized);
        }

        _profile.AppendLine();
    }

    private void AddWriteRules()
    {
        _profile.AppendLine("; File write");

        // First, allow writes to specific paths
        foreach (var path in _allowedWritePaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddAllowRule("file-write*", normalized);
        }

        // Always allow /tmp
        _profile.AppendLine("(allow file-write* (subpath \"/tmp\"))");
        _profile.AppendLine("(allow file-write* (subpath \"/private/tmp\"))");

        // Handle macOS TMPDIR pattern
        var tmpdir = System.Environment.GetEnvironmentVariable("TMPDIR");
        if (!string.IsNullOrEmpty(tmpdir) && tmpdir.Contains("/var/folders/"))
        {
            var parent = Path.GetDirectoryName(tmpdir.TrimEnd('/'));
            if (parent != null)
            {
                _profile.AppendLine($"(allow file-write* (subpath \"{parent}\"))");
            }
        }

        _profile.AppendLine();

        // Then deny specific paths (takes precedence)
        _profile.AppendLine("; Denied write paths");
        foreach (var path in _deniedWritePaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddDenyRule("file-write*", normalized);
        }

        _profile.AppendLine();

        // Add move-blocking rules
        _profile.AppendLine("; Move-blocking rules (prevent bypass via mv/rename)");
        AddMoveBlockingRules();

        _profile.AppendLine();
    }

    private void AddMoveBlockingRules()
    {
        // Combine denied read and write paths for move protection. Read-deny
        // regions need create/unlink protection too, otherwise a process can
        // move a replacement path into a denied location.
        var protectedPaths = _deniedWritePaths
            .Concat(_deniedReadPaths)
            .Distinct();

        foreach (var path in protectedPaths)
        {
            var normalized = PathNormalizer.Normalize(path);

            AddMoveBlockingDenyRule(normalized, "file-write-unlink");
            AddMoveBlockingDenyRule(normalized, "file-write-create");

            // Block moving ancestor directories
            foreach (var ancestor in PathNormalizer.GetAncestors(normalized))
            {
                AddMoveBlockingDenyRule(ancestor, "file-write-unlink", literal: true);
                AddMoveBlockingDenyRule(ancestor, "file-write-create", literal: true);
            }
        }

        // Re-allow create/unlink for explicit write roots. This matches the
        // reference ordering nuance: broad read-deny move blocking should not
        // accidentally make an explicitly writable subtree unusable.
        foreach (var path in _allowedWritePaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddMoveBlockingAllowRule(normalized, "file-write-unlink");
            AddMoveBlockingAllowRule(normalized, "file-write-create");
        }

        // Then re-deny explicit write-deny paths so deny-within-allow remains
        // stronger than the allow-back above.
        foreach (var path in _deniedWritePaths)
        {
            var normalized = PathNormalizer.Normalize(path);
            AddMoveBlockingDenyRule(normalized, "file-write-unlink");
            AddMoveBlockingDenyRule(normalized, "file-write-create");
        }
    }

    private void AddMoveBlockingDenyRule(string path, string operation, bool literal = false)
    {
        AddMoveBlockingRule("deny", operation, path, literal);
    }

    private void AddMoveBlockingAllowRule(string path, string operation)
    {
        AddMoveBlockingRule("allow", operation, path, literal: false);
    }

    private void AddMoveBlockingRule(string action, string operation, string path, bool literal)
    {
        if (!literal && GlobToRegex.ContainsGlobChars(path))
        {
            var regex = GlobToRegex.ConvertAndEscape(path);
            _profile.AppendLine($"({action} {operation}");
            _profile.AppendLine($"  (regex #\"{regex}\")");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
        else
        {
            var pathKind = literal ? "literal" : "subpath";
            _profile.AppendLine($"({action} {operation}");
            _profile.AppendLine($"  ({pathKind} {EscapePath(path)})");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
    }

    private void AddPtySupport()
    {
        _profile.AppendLine("; Pseudo-terminal (pty) support");
        _profile.AppendLine("(allow pseudo-tty)");
        _profile.AppendLine("(allow file-ioctl");
        _profile.AppendLine("  (literal \"/dev/ptmx\")");
        _profile.AppendLine("  (regex #\"^/dev/ttys\")");
        _profile.AppendLine(")");
        _profile.AppendLine("(allow file-read* file-write*");
        _profile.AppendLine("  (literal \"/dev/ptmx\")");
        _profile.AppendLine("  (regex #\"^/dev/ttys\")");
        _profile.AppendLine(")");
    }

    private void AddUnixSocketRules()
    {
        if (!_allowAllUnixSockets && _allowedUnixSockets.Count == 0)
            return;

        _profile.AppendLine();
        _profile.AppendLine("; Unix socket rules");

        if (_allowAllUnixSockets)
        {
            _profile.AppendLine("(allow system-socket (socket-domain AF_UNIX))");
            _profile.AppendLine("(allow network-bind (local unix-socket \"*\"))");
            _profile.AppendLine("(allow network-outbound (remote unix-socket \"*\"))");
        }
        else
        {
            // Allow only specific socket paths
            // Note: don't resolve symlinks for socket paths - keep them as specified
            _profile.AppendLine("(allow system-socket (socket-domain AF_UNIX))");
            foreach (var socketPath in _allowedUnixSockets)
            {
                var normalized = PathNormalizer.Normalize(socketPath, resolveSymlinks: false);

                // Allow read/write access to the socket file
                _profile.AppendLine($"(allow file-read* file-write* (literal \"{normalized}\"))");

                // Allow network operations on the socket
                _profile.AppendLine($"(allow network-bind (local unix-socket \"{normalized}\"))");
                _profile.AppendLine($"(allow network-outbound (remote unix-socket \"{normalized}\"))");
            }
        }
    }

    private void AddConfiguredMachLookupRules()
    {
        if (_allowedMachLookups.Count == 0)
            return;

        _profile.AppendLine();
        _profile.AppendLine("; Configured Mach lookup rules");

        if (_allowedMachLookups.Contains("*"))
        {
            _profile.AppendLine("(allow mach-lookup)");
            return;
        }

        _profile.AppendLine("(allow mach-lookup");
        foreach (var pattern in _allowedMachLookups.Order(StringComparer.Ordinal))
        {
            if (pattern.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1];
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + ".*$";
                _profile.AppendLine($"  (regex #\"{regex}\")");
            }
            else
            {
                _profile.AppendLine($"  (global-name \"{pattern}\")");
            }
        }
        _profile.AppendLine(")");
    }

    private void AddAllowRule(string operation, string path)
    {
        if (GlobToRegex.ContainsGlobChars(path))
        {
            var regex = GlobToRegex.ConvertAndEscape(path);
            _profile.AppendLine($"(allow {operation}");
            _profile.AppendLine($"  (regex #\"{regex}\")");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
        else
        {
            _profile.AppendLine($"(allow {operation}");
            _profile.AppendLine($"  (subpath {EscapePath(path)})");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
    }

    private void AddDenyRule(string operation, string path)
    {
        if (GlobToRegex.ContainsGlobChars(path))
        {
            var regex = GlobToRegex.ConvertAndEscape(path);
            _profile.AppendLine($"(deny {operation}");
            _profile.AppendLine($"  (regex #\"{regex}\")");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
        else
        {
            _profile.AppendLine($"(deny {operation}");
            _profile.AppendLine($"  (subpath {EscapePath(path)})");
            _profile.AppendLine($"  (with message \"{_logTag}\"))");
        }
    }

    private static string EscapePath(string path)
    {
        // Use JSON encoding for proper escaping
        return System.Text.Json.JsonSerializer.Serialize(path);
    }
}
