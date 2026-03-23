using System.Collections.Concurrent;

namespace HPD.Sandbox.Local.Security;

/// <summary>
/// Scans directories to find dangerous files that need protection.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b></para>
/// <para>Uses parallel directory enumeration with configurable depth limiting.</para>
/// <para>Results are cached per working directory to avoid repeated scans.</para>
///
/// <para><b>Why Scanning is Necessary:</b></para>
/// <para>
/// Dangerous files like .gitconfig can exist in subdirectories (nested git repos).
/// We need to find ALL instances, not just the ones in the working directory root.
/// </para>
/// </remarks>
public sealed class DangerousPathScanner
{
    private readonly int _maxDepth;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new();

    /// <summary>
    /// Creates a scanner with the specified maximum search depth.
    /// </summary>
    /// <param name="maxDepth">Maximum directory depth to search (default: 3)</param>
    public DangerousPathScanner(int maxDepth = 3)
    {
        _maxDepth = maxDepth;
    }

    /// <summary>
    /// Gets all dangerous paths that should be write-protected.
    /// </summary>
    /// <param name="workingDirectory">The root directory to scan</param>
    /// <param name="allowGitConfig">Whether to allow .git/config writes (for git remote operations)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of absolute paths to protect</returns>
    public async Task<IReadOnlyList<string>> GetDangerousPathsAsync(
        string workingDirectory,
        bool allowGitConfig = false,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{workingDirectory}:{allowGitConfig}:{_maxDepth}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var paths = await ScanAsync(workingDirectory, allowGitConfig, cancellationToken);
        _cache.TryAdd(cacheKey, paths);

        return paths;
    }

    /// <summary>
    /// Clears the scan cache (call when working directory changes).
    /// </summary>
    public void ClearCache() => _cache.Clear();

    private async Task<IReadOnlyList<string>> ScanAsync(
        string workingDirectory,
        bool allowGitConfig,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<string>();
        var normalizedRoot = PathNormalizer.Normalize(workingDirectory);

        // Add static paths in the root directory
        foreach (var file in SandboxDefaults.DangerousFiles)
        {
            var path = Path.Combine(normalizedRoot, file);
            if (File.Exists(path))
                results.Add(path);
        }

        foreach (var dir in SandboxDefaults.DangerousDirectories)
        {
            var path = Path.Combine(normalizedRoot, dir);
            if (Directory.Exists(path))
                results.Add(path);
        }

        // Always protect .git/hooks in root
        var rootGitHooks = Path.Combine(normalizedRoot, ".git", "hooks");
        if (Directory.Exists(rootGitHooks))
            results.Add(rootGitHooks);

        // Conditionally protect .git/config
        if (!allowGitConfig)
        {
            var rootGitConfig = Path.Combine(normalizedRoot, ".git", "config");
            if (File.Exists(rootGitConfig))
                results.Add(rootGitConfig);
        }

        // Scan subdirectories in parallel
        await ScanDirectoryAsync(normalizedRoot, 0, allowGitConfig, results, cancellationToken);

        return results.Distinct().ToList();
    }

    private async Task ScanDirectoryAsync(
        string directory,
        int currentDepth,
        bool allowGitConfig,
        ConcurrentBag<string> results,
        CancellationToken cancellationToken)
    {
        if (currentDepth >= _maxDepth)
            return;

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        var tasks = new List<Task>();

        foreach (var subdir in subdirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subdir);

            // Skip node_modules and other heavy directories
            if (ShouldSkipDirectory(dirName))
                continue;

            // Check for dangerous files in this directory
            foreach (var file in SandboxDefaults.DangerousFiles)
            {
                var path = Path.Combine(subdir, file);
                if (File.Exists(path))
                    results.Add(path);
            }

            // Check for dangerous directories
            foreach (var dangerousDir in SandboxDefaults.DangerousDirectories)
            {
                var path = Path.Combine(subdir, dangerousDir);
                if (Directory.Exists(path))
                    results.Add(path);
            }

            // Check for nested .git/hooks (nested git repos)
            var gitHooks = Path.Combine(subdir, ".git", "hooks");
            if (Directory.Exists(gitHooks))
                results.Add(gitHooks);

            if (!allowGitConfig)
            {
                var gitConfig = Path.Combine(subdir, ".git", "config");
                if (File.Exists(gitConfig))
                    results.Add(gitConfig);
            }

            // Recurse
            tasks.Add(ScanDirectoryAsync(subdir, currentDepth + 1, allowGitConfig, results, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private static bool ShouldSkipDirectory(string dirName)
    {
        // Skip directories that are unlikely to contain dangerous files
        // and would significantly slow down scanning
        return dirName is
            "node_modules" or
            "vendor" or
            ".git" or  // We handle .git specially
            "bin" or
            "obj" or
            "packages" or
            ".nuget" or
            ".npm" or
            ".cache" or
            "__pycache__" or
            ".venv" or
            "venv" or
            "target" or  // Rust/Java
            "build" or
            "dist";
    }
}
