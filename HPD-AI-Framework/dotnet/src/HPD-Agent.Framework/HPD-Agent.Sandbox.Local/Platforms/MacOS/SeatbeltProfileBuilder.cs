using System.Text;
using HPD.Sandbox.Local.Security;

namespace HPD.Sandbox.Local.Platforms.MacOS;

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
    private readonly StringBuilder _profile = new();
    private readonly string _logTag;
    private readonly HashSet<string> _allowedWritePaths = [];
    private readonly HashSet<string> _deniedWritePaths = [];
    private readonly HashSet<string> _deniedReadPaths = [];
    private readonly HashSet<string> _allowedUnixSockets = [];
    private bool _networkAllowed = true;
    private int? _httpProxyPort;
    private int? _socksProxyPort;
    private bool _allowPty;
    private bool _allowLocalBinding;
    private bool _allowAllUnixSockets;

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
        _profile.AppendLine("  (global-name \"com.apple.trustd.agent\")");
        _profile.AppendLine("  (global-name \"com.apple.SecurityServer\")");
        _profile.AppendLine(")");
        _profile.AppendLine();
        _profile.AppendLine("; POSIX IPC");
        _profile.AppendLine("(allow ipc-posix-shm)");
        _profile.AppendLine("(allow ipc-posix-sem)");
        _profile.AppendLine();
        _profile.AppendLine("; IOKit");
        _profile.AppendLine("(allow iokit-get-properties)");
        _profile.AppendLine();
        _profile.AppendLine("; Sysctl read");
        _profile.AppendLine("(allow sysctl-read)");
        _profile.AppendLine();
        _profile.AppendLine("; Device I/O");
        _profile.AppendLine("(allow file-ioctl (literal \"/dev/null\"))");
        _profile.AppendLine("(allow file-ioctl (literal \"/dev/tty\"))");
        _profile.AppendLine();
    }

    private void AddNetworkRules()
    {
        _profile.AppendLine("; Network");

        if (!_networkAllowed && _httpProxyPort == null)
        {
            _profile.AppendLine("(deny network*)");
        }
        else
        {
            _profile.AppendLine("(allow network*)");

            // Allow local binding if requested
            if (_allowLocalBinding)
            {
                _profile.AppendLine("(allow network-bind (local ip \"localhost:*\"))");
                _profile.AppendLine("(allow network-inbound (local ip \"localhost:*\"))");
            }

            // Allow proxy ports
            if (_httpProxyPort.HasValue)
            {
                _profile.AppendLine($"(allow network-outbound (remote ip \"localhost:{_httpProxyPort}\"))");
            }
            if (_socksProxyPort.HasValue)
            {
                _profile.AppendLine($"(allow network-outbound (remote ip \"localhost:{_socksProxyPort}\"))");
            }
        }

        _profile.AppendLine();
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
        var tmpdir = Environment.GetEnvironmentVariable("TMPDIR");
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
        // Combine denied read and write paths for move protection
        var protectedPaths = _deniedWritePaths
            .Concat(_deniedReadPaths)
            .Distinct();

        foreach (var path in protectedPaths)
        {
            var normalized = PathNormalizer.Normalize(path);

            // Block moving/unlinking the path itself
            if (GlobToRegex.ContainsGlobChars(normalized))
            {
                var regex = GlobToRegex.ConvertAndEscape(normalized);
                _profile.AppendLine($"(deny file-write-unlink");
                _profile.AppendLine($"  (regex #\"{regex}\")");
                _profile.AppendLine($"  (with message \"{_logTag}\"))");
            }
            else
            {
                _profile.AppendLine($"(deny file-write-unlink");
                _profile.AppendLine($"  (subpath {EscapePath(normalized)})");
                _profile.AppendLine($"  (with message \"{_logTag}\"))");
            }

            // Block moving ancestor directories
            foreach (var ancestor in PathNormalizer.GetAncestors(normalized))
            {
                _profile.AppendLine($"(deny file-write-unlink");
                _profile.AppendLine($"  (literal {EscapePath(ancestor)})");
                _profile.AppendLine($"  (with message \"{_logTag}\"))");
            }
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
            // Allow all Unix socket operations
            _profile.AppendLine("(allow network-outbound (socket-domain AF_UNIX))");
            _profile.AppendLine("(allow network-inbound (socket-domain AF_UNIX))");
            _profile.AppendLine("(allow network-bind (socket-domain AF_UNIX))");
        }
        else
        {
            // Allow only specific socket paths
            foreach (var socketPath in _allowedUnixSockets)
            {
                var normalized = PathNormalizer.Normalize(socketPath);
                var escaped = EscapePath(normalized);

                // Allow read/write access to the socket file
                _profile.AppendLine($"(allow file-read* file-write* (literal {escaped}))");

                // Allow network operations on the socket
                _profile.AppendLine($"(allow network-outbound (literal {escaped}))");
            }
        }
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
