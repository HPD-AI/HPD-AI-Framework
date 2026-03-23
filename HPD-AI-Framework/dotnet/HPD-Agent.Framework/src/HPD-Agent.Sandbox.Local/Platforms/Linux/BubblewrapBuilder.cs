using System.Text;
using HPD.Sandbox.Local.Security;

namespace HPD.Sandbox.Local.Platforms.Linux;

/// <summary>
/// Fluent builder for bubblewrap (bwrap) command arguments.
/// </summary>
/// <remarks>
/// <para><b>Bubblewrap Features Used:</b></para>
/// <list type="bullet">
/// <item>--ro-bind: Read-only bind mount</item>
/// <item>--bind: Read-write bind mount</item>
/// <item>--tmpfs: Mount tmpfs (blocks reads)</item>
/// <item>--unshare-net: Network namespace isolation</item>
/// <item>--unshare-pid: PID namespace isolation</item>
/// <item>--proc: Mount /proc in the new namespace</item>
/// <item>--dev: Mount /dev with standard devices</item>
/// <item>--setenv: Set environment variable</item>
/// <item>--die-with-parent: Kill sandbox if parent dies</item>
/// </list>
/// </remarks>
public sealed class BubblewrapBuilder
{
    private readonly List<string> _args = [];
    private readonly HashSet<string> _writablePaths = [];
    private bool _networkIsolated;

    /// <summary>
    /// Creates a new builder with secure defaults.
    /// </summary>
    public BubblewrapBuilder()
    {
        // Essential safety options
        _args.Add("--new-session");
        _args.Add("--die-with-parent");
    }

    /// <summary>
    /// Sets up the root filesystem as read-only with selective write permissions.
    /// </summary>
    public BubblewrapBuilder WithReadOnlyRoot()
    {
        _args.AddRange(["--ro-bind", "/", "/"]);
        return this;
    }

    /// <summary>
    /// Allows writes to a specific path.
    /// </summary>
    /// <param name="path">Path to allow writes to</param>
    public BubblewrapBuilder WithWritablePath(string path)
    {
        var normalized = PathNormalizer.Normalize(path, resolveSymlinks: true);

        if (!Directory.Exists(normalized) && !File.Exists(normalized))
            return this; // Skip non-existent paths

        // Skip /dev paths (handled separately)
        if (normalized.StartsWith("/dev/"))
            return this;

        if (_writablePaths.Add(normalized))
        {
            _args.AddRange(["--bind", normalized, normalized]);
        }

        return this;
    }

    /// <summary>
    /// Allows writes to multiple paths.
    /// </summary>
    public BubblewrapBuilder WithWritablePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            WithWritablePath(path);
        return this;
    }

    /// <summary>
    /// Denies read access to a path by mounting tmpfs over it.
    /// </summary>
    /// <param name="path">Path to hide</param>
    public BubblewrapBuilder WithDeniedReadPath(string path)
    {
        var normalized = PathNormalizer.Normalize(path, resolveSymlinks: true);

        if (!Directory.Exists(normalized) && !File.Exists(normalized))
            return this;

        if (Directory.Exists(normalized))
        {
            _args.AddRange(["--tmpfs", normalized]);
        }
        else
        {
            // For files, bind /dev/null over them
            _args.AddRange(["--ro-bind", "/dev/null", normalized]);
        }

        return this;
    }

    /// <summary>
    /// Denies write access to a path (makes it read-only even within a writable parent).
    /// </summary>
    /// <param name="path">Path to protect</param>
    public BubblewrapBuilder WithDeniedWritePath(string path)
    {
        var normalized = PathNormalizer.Normalize(path, resolveSymlinks: true);

        if (!Directory.Exists(normalized) && !File.Exists(normalized))
            return this;

        // Only apply if within a writable path
        if (_writablePaths.Any(wp => PathNormalizer.IsWithinPath(normalized, wp)))
        {
            _args.AddRange(["--ro-bind", normalized, normalized]);
        }

        return this;
    }

    /// <summary>
    /// Denies write access to multiple paths.
    /// </summary>
    public BubblewrapBuilder WithDeniedWritePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            WithDeniedWritePath(path);
        return this;
    }

    /// <summary>
    /// Isolates the network namespace (no network access unless bridges are set up).
    /// </summary>
    public BubblewrapBuilder WithNetworkIsolation()
    {
        if (!_networkIsolated)
        {
            _args.Add("--unshare-net");
            _networkIsolated = true;
        }
        return this;
    }

    /// <summary>
    /// Adds Unix socket bind mounts for network proxy access.
    /// </summary>
    public BubblewrapBuilder WithUnixSocketBinds(IEnumerable<string> socketPaths)
    {
        foreach (var socketPath in socketPaths)
        {
            if (File.Exists(socketPath))
            {
                _args.AddRange(["--bind", socketPath, socketPath]);
            }
        }
        return this;
    }

    /// <summary>
    /// Sets an environment variable in the sandbox.
    /// </summary>
    public BubblewrapBuilder WithEnvironmentVariable(string name, string value)
    {
        _args.AddRange(["--setenv", name, value]);
        return this;
    }

    /// <summary>
    /// Sets multiple environment variables.
    /// </summary>
    public BubblewrapBuilder WithEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var (name, value) in variables)
            WithEnvironmentVariable(name, value);
        return this;
    }

    /// <summary>
    /// Passes through an environment variable from the host.
    /// </summary>
    public BubblewrapBuilder WithPassthroughEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value != null)
            WithEnvironmentVariable(name, value);
        return this;
    }

    /// <summary>
    /// Passes through safe environment variables.
    /// </summary>
    public BubblewrapBuilder WithSafeEnvironmentVariables()
    {
        foreach (var name in SandboxDefaults.SafeEnvironmentVariables)
            WithPassthroughEnvironmentVariable(name);
        return this;
    }

    /// <summary>
    /// Isolates the PID namespace.
    /// </summary>
    /// <param name="mountProc">Whether to mount a fresh /proc (required for full isolation)</param>
    public BubblewrapBuilder WithPidIsolation(bool mountProc = true)
    {
        _args.Add("--unshare-pid");
        if (mountProc)
            _args.AddRange(["--proc", "/proc"]);
        return this;
    }

    /// <summary>
    /// Uses weaker isolation for nested sandbox environments (e.g., Docker).
    /// </summary>
    public BubblewrapBuilder WithWeakerNestedSandbox()
    {
        // Skip --unshare-pid and --proc which require privileges
        // Network isolation may also not work in some container environments
        return this;
    }

    /// <summary>
    /// Sets up standard device access.
    /// </summary>
    public BubblewrapBuilder WithDevices()
    {
        _args.AddRange(["--dev", "/dev"]);
        return this;
    }

    /// <summary>
    /// Sets up a tmpfs at /tmp.
    /// </summary>
    public BubblewrapBuilder WithTmpfs(string path = "/tmp")
    {
        _args.AddRange(["--tmpfs", path]);
        return this;
    }

    /// <summary>
    /// Builds the final bwrap command.
    /// </summary>
    /// <param name="command">The user command to run</param>
    /// <param name="shell">Shell to use (default: /bin/sh)</param>
    /// <returns>Complete bwrap command string</returns>
    public string Build(string command, string shell = "/bin/sh")
    {
        var args = new List<string>(_args)
        {
            "--",
            shell,
            "-c",
            command
        };

        return $"bwrap {string.Join(" ", args.Select(QuoteArg))}";
    }

    /// <summary>
    /// Builds the bwrap command with a setup script prefix.
    /// </summary>
    /// <param name="setupScript">Script to run before the user command</param>
    /// <param name="command">The user command</param>
    /// <param name="shell">Shell to use</param>
    public string BuildWithSetup(string setupScript, string command, string shell = "/bin/sh")
    {
        var fullScript = $"{setupScript}\n{command}";
        return Build(fullScript, shell);
    }

    /// <summary>
    /// Builds the bwrap command with seccomp filter applied to user command.
    /// </summary>
    /// <param name="setupScript">Script to run BEFORE seccomp (e.g., start socat bridges)</param>
    /// <param name="command">User command to run AFTER seccomp is applied</param>
    /// <param name="seccompHelperPath">Path to the apply-seccomp binary</param>
    /// <param name="shell">Shell to use</param>
    /// <remarks>
    /// <para>The setup script runs without seccomp restrictions, allowing socat
    /// to create Unix sockets for the network bridges.</para>
    /// <para>The user command runs through apply-seccomp, which blocks Unix socket creation.</para>
    /// </remarks>
    public string BuildWithSeccomp(string setupScript, string command, string seccompHelperPath, string shell = "/bin/sh")
    {
        // Setup script runs first (can create Unix sockets)
        // Then apply-seccomp applies the filter and execs the user command
        var fullScript = $"{setupScript}\nexec {QuoteArg(seccompHelperPath)} {shell} -c {QuoteArg(command)}";
        return Build(fullScript, shell);
    }

    /// <summary>
    /// Gets the current arguments (for debugging).
    /// </summary>
    public IReadOnlyList<string> GetArguments() => _args.AsReadOnly();

    /// <summary>
    /// Safely quotes a shell argument.
    /// </summary>
    private static string QuoteArg(string arg)
    {
        // Use single quotes with escaped single quotes
        return $"'{arg.Replace("'", "'\\''")}'";
    }
}
