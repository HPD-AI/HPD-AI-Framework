using System.Runtime.InteropServices;

namespace HPD.Sandbox.Local.Security;

/// <summary>
/// Normalizes paths for secure comparison and sandbox configuration.
/// </summary>
/// <remarks>
/// <para><b>Security Purpose:</b></para>
/// <para>Prevents sandbox bypass via:</para>
/// <list type="bullet">
/// <item>Mixed-case paths on case-insensitive filesystems (macOS/Windows)</item>
/// <item>Symlinks pointing outside allowed directories</item>
/// <item>Relative path traversal (../../../etc/passwd)</item>
/// <item>Tilde expansion (~/.ssh)</item>
/// </list>
/// </remarks>
public static class PathNormalizer
{
    /// <summary>
    /// Whether the current filesystem is case-insensitive.
    /// </summary>
    public static bool IsCaseInsensitive { get; } = DetectCaseInsensitivity();

    /// <summary>
    /// Normalizes a path for sandbox configuration.
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <param name="resolveSymlinks">Whether to resolve symlinks to real paths</param>
    /// <returns>Normalized absolute path</returns>
    public static string Normalize(string path, bool resolveSymlinks = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        var normalized = path;

        // 1. Expand tilde
        if (normalized == "~")
        {
            normalized = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (normalized.StartsWith("~/"))
        {
            normalized = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                normalized[2..]);
        }

        // 2. Expand environment variables
        normalized = Environment.ExpandEnvironmentVariables(normalized);

        // 3. Convert to absolute path
        if (!Path.IsPathRooted(normalized))
        {
            normalized = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
        }
        else
        {
            normalized = Path.GetFullPath(normalized);
        }

        // 4. Resolve symlinks if requested and path exists
        if (resolveSymlinks && (File.Exists(normalized) || Directory.Exists(normalized)))
        {
            normalized = ResolveSymlinks(normalized);
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes a path for case-insensitive comparison.
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>Lowercase path on case-insensitive systems, original otherwise</returns>
    public static string NormalizeForComparison(string path)
    {
        // Always normalize to lowercase for security
        // This prevents bypass via .cLauDe/Settings.json on macOS
        return path.ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a path is within (or equal to) a base path.
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <param name="basePath">The base path</param>
    /// <returns>True if path is within or equal to basePath</returns>
    public static bool IsWithinPath(string path, string basePath)
    {
        var normalizedPath = NormalizeForComparison(Normalize(path));
        var normalizedBase = NormalizeForComparison(Normalize(basePath));

        // Ensure base path ends with separator for proper prefix matching
        if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
            normalizedBase += Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedBase, StringComparison.Ordinal) ||
               normalizedPath == normalizedBase.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Gets all ancestor directories for a path (for move-blocking rules).
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns>Ancestor directories, closest first</returns>
    public static IEnumerable<string> GetAncestors(string path)
    {
        var normalized = Normalize(path);
        var current = Path.GetDirectoryName(normalized);

        while (!string.IsNullOrEmpty(current) && current != Path.GetPathRoot(current))
        {
            yield return current;
            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>
    /// Checks if a path contains glob characters.
    /// </summary>
    public static bool ContainsGlobChars(string path)
    {
        return path.Contains('*') || path.Contains('?') || path.Contains('[');
    }

    /// <summary>
    /// Removes trailing /** glob suffix from a path pattern.
    /// </summary>
    public static string RemoveTrailingGlobSuffix(string path)
    {
        if (path.EndsWith("/**"))
            return path[..^3];
        if (path.EndsWith("/*"))
            return path[..^2];
        return path;
    }

    private static string ResolveSymlinks(string path)
    {
        try
        {
            // On Unix, use realpath equivalent
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var info = new FileInfo(path);
                if (info.LinkTarget != null)
                {
                    // Resolve the link target
                    var resolved = Path.IsPathRooted(info.LinkTarget)
                        ? info.LinkTarget
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, info.LinkTarget));

                    // Recursively resolve in case of chained symlinks
                    return ResolveSymlinks(resolved);
                }

                // Check if any component is a symlink
                var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var currentPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : "/";

                foreach (var part in parts)
                {
                    currentPath = Path.Combine(currentPath, part);
                    if (Directory.Exists(currentPath))
                    {
                        var dirInfo = new DirectoryInfo(currentPath);
                        if (dirInfo.LinkTarget != null)
                        {
                            var resolved = Path.IsPathRooted(dirInfo.LinkTarget)
                                ? dirInfo.LinkTarget
                                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(currentPath)!, dirInfo.LinkTarget));

                            // Continue with resolved path
                            var remaining = string.Join(Path.DirectorySeparatorChar,
                                parts.Skip(Array.IndexOf(parts, part) + 1));
                            return ResolveSymlinks(Path.Combine(resolved, remaining));
                        }
                    }
                }
            }

            return path;
        }
        catch
        {
            // If we can't resolve, return original
            return path;
        }
    }

    private static bool DetectCaseInsensitivity()
    {
        // macOS and Windows are typically case-insensitive
        // Linux is typically case-sensitive
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        // For unknown platforms, assume case-sensitive (safer)
        return false;
    }
}
