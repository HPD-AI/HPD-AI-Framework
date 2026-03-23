namespace HPD.Sandbox.Local.Security;

/// <summary>
/// Security defaults for sandbox configuration.
/// These paths are ALWAYS protected regardless of user configuration.
/// </summary>
/// <remarks>
/// <para><b>Rationale:</b></para>
/// <para>These files can be used for code execution or credential theft:</para>
/// <list type="bullet">
/// <item>.gitconfig/.gitmodules - Can execute arbitrary code via hooks</item>
/// <item>.bashrc/.zshrc - Execute on shell startup</item>
/// <item>.ssh - Private keys and authorized_keys</item>
/// <item>.gnupg - GPG private keys</item>
/// <item>.aws/.azure/.gcloud - Cloud credentials</item>
/// </list>
/// </remarks>
public static class SandboxDefaults
{
    /// <summary>
    /// Files that should be protected from writes.
    /// These can be used for code execution or data exfiltration.
    /// </summary>
    public static readonly IReadOnlyList<string> DangerousFiles =
    [
        ".gitconfig",
        ".gitmodules",
        ".bashrc",
        ".bash_profile",
        ".bash_login",
        ".bash_logout",
        ".zshrc",
        ".zprofile",
        ".zlogin",
        ".zlogout",
        ".profile",
        ".login",
        ".logout",
        ".inputrc",
        ".ripgreprc",
        ".mcp.json",
        ".npmrc",           // Can redirect package installs
        ".yarnrc",
        ".nugetrc",
        ".pip.conf",
        ".condarc",
    ];

    /// <summary>
    /// Directories that should be protected from writes.
    /// </summary>
    public static readonly IReadOnlyList<string> DangerousDirectories =
    [
        ".git/hooks",       // Git hooks execute on various git operations
        ".vscode",          // VS Code tasks can execute arbitrary code
        ".idea",            // IntelliJ run configurations
        ".claude/commands", // Claude custom commands
        ".claude/agents",   // Claude agent configurations
    ];

    /// <summary>
    /// Directories containing sensitive credentials (deny read by default).
    /// </summary>
    public static readonly IReadOnlyList<string> SensitiveDirectories =
    [
        "~/.ssh",
        "~/.gnupg",
        "~/.aws",
        "~/.azure",
        "~/.config/gcloud",
        "~/.kube",
        "~/.docker",
        "~/.vault-token",
        "~/.netrc",
        "~/.git-credentials",
    ];

    /// <summary>
    /// System paths that should always be writable for commands to work.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultWritePaths =
    [
        "/dev/stdout",
        "/dev/stderr",
        "/dev/null",
        "/dev/tty",
        "/tmp",
        "/private/tmp",     // macOS
        "/var/tmp",
    ];

    /// <summary>
    /// Environment variables safe to pass through to sandboxed processes.
    /// </summary>
    public static readonly IReadOnlyList<string> SafeEnvironmentVariables =
    [
        "PATH",
        "HOME",
        "USER",
        "LANG",
        "LC_ALL",
        "TERM",
        "SHELL",
        "EDITOR",
        "VISUAL",
        "TZ",
        "TMPDIR",
        "XDG_RUNTIME_DIR",
        "XDG_CONFIG_HOME",
        "XDG_DATA_HOME",
        "XDG_CACHE_HOME",
        // Development tools
        "DOTNET_ROOT",
        "DOTNET_CLI_HOME",
        "NUGET_PACKAGES",
        "NODE_PATH",
        "NPM_CONFIG_PREFIX",
        "GOPATH",
        "CARGO_HOME",
        "RUSTUP_HOME",
    ];

    /// <summary>
    /// Domains that should never be blocked (infrastructure).
    /// </summary>
    public static readonly IReadOnlyList<string> InfrastructureDomains =
    [
        "localhost",
        "127.0.0.1",
        "::1",
    ];

    /// <summary>
    /// Common development domains for permissive profiles.
    /// </summary>
    public static readonly IReadOnlyList<string> CommonDevDomains =
    [
        // Package registries
        "*.npmjs.org",
        "registry.npmjs.org",
        "*.nuget.org",
        "api.nuget.org",
        "*.pypi.org",
        "pypi.org",
        "files.pythonhosted.org",
        // Source control
        "github.com",
        "*.github.com",
        "gitlab.com",
        "*.gitlab.com",
        "bitbucket.org",
        "*.bitbucket.org",
        // Container registries
        "*.docker.io",
        "*.docker.com",
        "ghcr.io",
        "*.gcr.io",
        // CDNs commonly used
        "*.cloudflare.com",
        "*.jsdelivr.net",
        "*.unpkg.com",
    ];
}
