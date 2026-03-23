using System.Collections.Generic;
using HPD.Agent;

namespace HPD.Agent.Toolkit.FileSystem;

/// <summary>
/// Context for FileSystem Toolkit operations with workspace validation.
/// </summary>
public class FileSystemContext : IToolMetadata
{
    private readonly Dictionary<string, object> _properties = new();

    /// <summary>
    /// Root directory of the workspace
    /// </summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Whether to allow operations outside the workspace (dangerous!)
    /// </summary>
    public bool AllowOutsideWorkspace { get; }

    /// <summary>
    /// Whether to respect .gitignore patterns
    /// </summary>
    public bool RespectGitIgnore { get; }

    /// <summary>
    /// Whether to respect .geminiignore patterns
    /// </summary>
    public bool RespectGeminiIgnore { get; }

    /// <summary>
    /// Maximum file size to read (in bytes)
    /// </summary>
    public long MaxFileSize { get; }

    /// <summary>
    /// Whether glob/search operations are enabled
    /// </summary>
    public bool EnableSearch { get; }

    /// <summary>
    /// Whether shell command execution is enabled
    /// </summary>
    public bool EnableShell { get; }

    /// <summary>
    /// Maximum timeout for shell commands (in seconds)
    /// </summary>
    public int MaxShellTimeoutSeconds { get; }

    /// <summary>
    /// Allowed shell commands (whitelist). If empty, all commands except blocklist are allowed.
    /// </summary>
    public HashSet<string> AllowedShellCommands { get; }

    /// <summary>
    /// Blocked shell commands (blacklist). These are always denied, even if in allowlist.
    /// </summary>
    public HashSet<string> BlockedShellCommands { get; }

    public FileSystemContext(
        string workspaceRoot,
        bool allowOutsideWorkspace = false,
        bool respectGitIgnore = true,
        bool respectGeminiIgnore = true,
        long maxFileSize = 10_000_000, // 10 MB default
        bool enableSearch = true,
        bool enableShell = false, // Shell disabled by default for security
        int maxShellTimeoutSeconds = 300, // 5 minutes max
        HashSet<string>? allowedShellCommands = null,
        HashSet<string>? blockedShellCommands = null)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        AllowOutsideWorkspace = allowOutsideWorkspace;
        RespectGitIgnore = respectGitIgnore;
        RespectGeminiIgnore = respectGeminiIgnore;
        MaxFileSize = maxFileSize;
        EnableSearch = enableSearch;
        EnableShell = enableShell;
        MaxShellTimeoutSeconds = maxShellTimeoutSeconds;

        // Initialize shell command lists with safe defaults
        AllowedShellCommands = allowedShellCommands ?? new HashSet<string>
        {
            // Safe read-only commands
            "ls", "cat", "head", "tail", "grep", "find", "which", "echo", "pwd", "wc", "sort", "uniq",

            // Version control
            "git",

            // Package managers (safe queries)
            "npm", "yarn", "pnpm", "dotnet", "node", "python", "pip", "cargo", "go",

            // Build tools
            "make", "cmake", "msbuild", "gradle", "mvn"
        };

        BlockedShellCommands = blockedShellCommands ?? new HashSet<string>
        {
            // Dangerous file operations
            "rm", "del", "format", "mkfs", "dd", "shred", "truncate",

            // Privilege escalation
            "sudo", "su", "doas",

            // Permission changes
            "chmod", "chown", "chgrp", "chattr",

            // Process control
            "kill", "killall", "pkill", "xkill",

            // System control
            "shutdown", "reboot", "halt", "poweroff", "init",

            // Network dangerous
            "nc", "netcat", "telnet", "curl", "wget" // Can download/execute code
        };

        // Populate properties dictionary
        _properties[nameof(WorkspaceRoot)] = WorkspaceRoot;
        _properties[nameof(AllowOutsideWorkspace)] = AllowOutsideWorkspace;
        _properties[nameof(RespectGitIgnore)] = RespectGitIgnore;
        _properties[nameof(RespectGeminiIgnore)] = RespectGeminiIgnore;
        _properties[nameof(MaxFileSize)] = MaxFileSize;
        _properties[nameof(EnableSearch)] = EnableSearch;
        _properties[nameof(EnableShell)] = EnableShell;
        _properties[nameof(MaxShellTimeoutSeconds)] = MaxShellTimeoutSeconds;
    }

    /// <summary>
    /// Validates that a path is within the workspace
    /// </summary>
    public bool IsPathWithinWorkspace(string path)
    {
        if (AllowOutsideWorkspace)
            return true;

        var fullPath = Path.GetFullPath(path);
        var workspaceFullPath = Path.GetFullPath(WorkspaceRoot);

        return fullPath.StartsWith(workspaceFullPath, StringComparison.OrdinalIgnoreCase);
    }

    #region IToolMetadata Implementation

    public T? GetProperty<T>(string propertyName, T? defaultValue = default)
    {
        if (_properties.TryGetValue(propertyName, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            if (typeof(T) == typeof(string))
                return (T)(object)value.ToString()!;
        }
        return defaultValue;
    }

    public bool HasProperty(string propertyName) => _properties.ContainsKey(propertyName);

    public IEnumerable<string> GetPropertyNames() => _properties.Keys;

    #endregion
}
